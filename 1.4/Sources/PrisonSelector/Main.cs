using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;
using FloatSubMenus;


namespace PrisonSelector
{
	[StaticConstructorOnStartup]
	public class Main
	{
		static Main()
		{
			new Harmony("PrisonSelector.Mod").PatchAll();
        }
    }
	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch(nameof(FloatMenuMakerMap.AddJobGiverWorkOrders))]
	static class PrisonSelector_AddJobGiverWorkOrders_Patch
	{
        public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
        {
            if (RoomMapper.prisUtilActive)
            {
                //FIX place these in a better place
                var overrideMenu = AccessTools.Method(RoomMapper.Assem.GetType("PrisonerUtil.InitialInteractionMode_Patches"), "OverrideMenuAddition");
                overrideMenu.Invoke(null, new object[] { true });
            }
            foreach (Thing thing in GenUI.ThingsUnderMouse(clickPos,1f,new TargetingParameters { 
                canTargetPawns = true,
                canTargetAnimals = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetMechs = false,
                
            }))
            {
                var optsList = new List<FloatMenuOption>();

                if (thing.Spawned )
                {
                    var targetPawn = thing as Pawn;

                    if (targetPawn.Downed || targetPawn.IsPrisoner)
                    {
                        
                        if (pawn.CanReach(targetPawn, PathEndMode.ClosestTouch, Danger.Deadly))
                        {
                            optsList = RoomMapper.getListOfPlaces(targetPawn, pawn);
                        }
                
               
                    if (optsList.Count() != 0)
                    {

                        var tmp = new List<FloatMenuOption>();

                        foreach (var opt in optsList)
                        {
                            //dunno why this is needed but w.e
                            tmp.Add(opt);
                        }
                        if (RoomMapper.CheckForActiveMod("brrainz.achtung"))
                        {
                            var menuAchtung = FloatSubMenu.CompatMMMCreate("Take " + targetPawn.Name.ToString().CapitalizeFirst() + " To", tmp);
                            opts.Add(menuAchtung);
                        }
                        else
                        {
                            var menu = new FloatSubMenu("Take " + targetPawn.Name.ToString().CapitalizeFirst() + " To", tmp);
                            opts.Add(menu);
                        }
                    }
                    optsList.Clear();
                    }
                }
            }
            
        }
    }
    public class RoomMapper
    {
        public static Dictionary<Room, RoomRoleDef> mapRooms;

        public static bool CheckForActiveMod(string id)
           => LoadedModManager.RunningMods.Any(x => x.PackageId == id);

        public static  readonly bool prisUtilActive = CheckForActiveMod("kathanon.prisonerutil");
        public static Assembly Assem = PrisUtilHelper();


        public static List<FloatMenuOption> getListOfPlaces(Pawn target, Pawn pawn)
        {
           
            var subOpts = new List<FloatMenuOption>();
            var roomPair= new List<Pair<Room,JobDef>>();

            MapRoomInMap(Find.CurrentMap);
            if (target.Downed || target.IsPrisoner)
            {
                if (!target.RaceProps.Animal)
                {
                    if (target.IsColonistPlayerControlled)
                    {
                        roomPair = mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.Hospital)
                                                .Select(x => new Pair<Room, JobDef>(x, JobDefOf.Rescue))
                                                .ToList();
                    }
                    else
                    {
                        if (target.Faction.AllyOrNeutralTo(Faction.OfPlayer) && !target.IsPrisoner)
                        {
                            roomPair = mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.Hospital)
                                                    .Select(x => new Pair<Room, JobDef>(x, JobDefOf.Rescue))
                                                    .ToList();
                        }
                        

                        roomPair.AddRange(mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.PrisonCell || p.Role == RoomRoleDefOf.PrisonBarracks )
                                               .Select(x => new Pair<Room, JobDef>(x, target.IsPrisoner? JobDefOf.Capture : JobDefOf.EscortPrisonerToBed))
                                               .ToList());
                    }
                }
                else
                {
                    roomPair = mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.Hospital || p.GetRoomRoleLabel().Contains("Barn") )
                                            .Select(x => new Pair<Room, JobDef>(x, JobDefOf.Rescue))
                                            .ToList();
                }
                short Index = 1;
                var prevRoomDef = JobDefOf.Rescue;
                foreach (var room in roomPair)
                {
                    if (prevRoomDef != room.Second)
                    {
                        Index = 1;
                    }

                    var bed = room.First.ContainedBeds.Where(r =>  target.RaceProps.Animal?!r.def.building.bed_humanlike:r.def.building.bed_humanlike
                                                                    && !r.AnyOccupants
                                                                    && !r.MapHeld.reservationManager.IsReservedByAnyoneOf(r, Faction.OfPlayer)
                                                                    && r != target.CurrentBed()).FirstOrFallback();
                    if (bed != null)
                    {
                        Job job = JobMaker.MakeJob(room.Second, target, bed);
                        job.count = 1;
                        
                        string label = string.Format("{0} {1}\n Take To {2} #{3}",
                                                        room.Second == JobDefOf.Rescue ? "Rescue" : "Capture",
                                                        target.Name.ToString(),
                                                        room.First.GetRoomRoleLabel(),
                                                        Index.ToString());

                        var outOpts = FloatMenuUtility.DecoratePrioritizedTask(
                            new FloatMenuOption(label, () =>
                            {
                                pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.MiscWork));
                            }
                            , MenuOptionPriority.RescueOrCapture
                                , (Rect obj) =>
                                {
                                    SimpleColor color = room.Second == JobDefOf.Rescue ? SimpleColor.Blue : SimpleColor.Red;
                                    GenDraw.DrawLineBetween(target.Position.ToVector3(), bed.Position.ToVector3(), color);
                                    GenDraw.DrawFieldEdges(bed.GetRoom().Cells.ToList(), color.ToUnityColor());
                                })
                            , pawn
                            , target);


                        if (prisUtilActive && room.Second != JobDefOf.Rescue)
                        {
                            
                            var method = AccessTools.Method(Assem.GetType("PrisonerUtil.InitialInteractionMode_Patches"), "InteractionSubMenu");
                            object[] args = { outOpts.Label.ToString(), outOpts.action };
                            object interOpts = method.Invoke(Assem.GetType("PrisonerUtil.InitialInteractionMode_Patches"), args);                           
                            subOpts.Add((FloatMenuOption)interOpts);
                        }
                        else
                        {
                            subOpts.Add(outOpts);
                        }
                    }
                    Index++;
                    prevRoomDef = room.Second;
                }
            }
            return subOpts;
        }

        public static Room GetValidRoomInMap(Building building, Map map)
        {
            if (building.Faction != Faction.OfPlayer)
                return null;

            if (building.Position.Fogged(map))
                return null;

            var room = building.Position.GetRoom(map);
            if (room == null || room.PsychologicallyOutdoors)
                return null;

            if (room.Role == RoomRoleDefOf.None)
                return null;

            return room;
        }

        public static void MapRoomInMap(Map map)
        {
            mapRooms = new Dictionary<Room, RoomRoleDef>();
               var listerBuildings = map.listerBuildings;
            foreach (var building in listerBuildings.allBuildingsColonist)
            {
                var room = GetValidRoomInMap(building, map);
                if (room == null)
                    continue;

                if (mapRooms.ContainsKey(room))
                    continue;

                mapRooms[room] = room.Role;
            }
        }

        public static Assembly PrisUtilHelper()
        {
            var prisUtilapp = AppDomain.CurrentDomain.GetAssemblies().FirstOrFallback(x => x.FullName.Contains("PrisonerUtil"));
            if (prisUtilapp != null)
            {
                
                //var type = prisUtilapp.GetTypes().First(p => p.Name.Contains("InitialInteractionMode_Patches"));

                //var patch = AccessTools.Method(prisUtilapp.GetType(),);
                //var method=type.GetMethods().First(p=> p.IsPublic && p.Name.Contains("InteractionSubMenu"));
                return prisUtilapp;
            }
            return null;
        }
    }


}
