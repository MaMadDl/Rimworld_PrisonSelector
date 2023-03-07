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
            foreach(Thing thing in GenUI.ThingsUnderMouse(clickPos,1f,new TargetingParameters { 
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetItems = false,
            }))
            {
                var optsList= new List<FloatMenuOption>();
                var targetPawn = thing as Pawn;
                if (targetPawn.Spawned )
                {   
                    if (targetPawn.Downed || targetPawn.IsPrisoner)
                    {
                        
                        if (pawn.CanReach(targetPawn, PathEndMode.ClosestTouch, Danger.Deadly))
                        {
                            optsList = RoomMapper.getListOfPlaces(targetPawn, pawn);
                        }
                    }
                }

                if (optsList.Count() != 0)
                {
                    
                    var tmp =new List<FloatMenuOption>();
                    
                    foreach (var opt in optsList)
                    {
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
                else
                {
                    var menu = new FloatMenuOption("No Valid Place/Bed to Take " + targetPawn.Name.ToString().CapitalizeFirst(), null);
                    opts.Add(menu);
                }
                optsList.Clear();
            }
        }
    }
    public class RoomMapper
    {
        public static Dictionary<Room, RoomRoleDef> mapRooms;
        
        private static Pawn targetPawn;
        private static Building_Bed targetBed;
        private static JobDef jobType;
        
        private const string PrisUtil_Id = "kathanon.PrisonerUtil";
        private const string Achtung_ID = "brrainz.achtung";

        public static bool CheckForActiveMod(string id)
           => LoadedModManager.RunningMods.Any(x => x.PackageId == id);

        public static List<FloatMenuOption> getListOfPlaces(Pawn target, Pawn pawn)
        {
            var subOpts = new List<FloatMenuOption>();
            var roomPair= new List<Pair<Room,JobDef>>();

            bool ofPlayer = target.IsColonistPlayerControlled;

            MapRoomInMap(Find.CurrentMap);

            if (target.Downed || target.IsPrisoner)
            {
                if (ofPlayer)
                {
                    roomPair = mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.Hospital)
                                            .Select(x => new Pair<Room, JobDef>(x, JobDefOf.Rescue))
                                            .ToList();
                }
                else
                {
                    if (target.Faction.AllyOrNeutralTo(Faction.OfPlayer))
                    {
                        roomPair = mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.Hospital)
                                                .Select(x => new Pair<Room, JobDef>(x, JobDefOf.Rescue))
                                                .ToList();
                    }
                    roomPair.AddRange(mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.PrisonCell || p.Role == RoomRoleDefOf.PrisonBarracks)
                                           .Select(x => new Pair<Room, JobDef>(x, JobDefOf.Capture))
                                           .ToList());
                }

                short Index = 1;
                foreach (var room in roomPair)
                {
                    if (room.First.ContainedBeds.Any(r => r.def.building.bed_humanlike && !r.AnyOccupants
                                                         && !r.MapHeld.reservationManager.IsReservedByAnyoneOf(r, Faction.OfPlayer)))
                    {

                        var bed = room.First.ContainedBeds.First(r => r.def.building.bed_humanlike
                                                                      && !r.AnyOccupants
                                                                      && !r.MapHeld.reservationManager.IsReservedByAnyoneOf(r, Faction.OfPlayer));
                        

                            Job job = JobMaker.MakeJob(room.Second, target, bed);
                            job.count = 1;
                            string label = string.Format("{0} {1}\n Take To {2} #{3}",
                                                         room.Second == JobDefOf.Rescue ? "Rescue" : "Capture",
                                                         target.Name.ToString(),
                                                         room.Second == JobDefOf.Rescue ? "Hospital" : "Prison",
                                                         Index.ToString());


                            subOpts.Add(FloatMenuUtility.DecoratePrioritizedTask(
                            new FloatMenuOption(label, delegate ()
                            {

                                pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.MiscWork)); 
                            
                            }, MenuOptionPriority.RescueOrCapture
                               // , mouseoverGuiAction: MouseAction
                                )
                            , pawn
                            , target));

                    }

                    Index++;
                }
            }
            return subOpts;
        }

        public static void MouseAction(Rect obj)
        {

            SimpleColor color = targetPawn.HostileTo(Faction.OfPlayer) ? SimpleColor.Red : SimpleColor.Blue;
            GenDraw.DrawLineBetween(targetPawn.Position.ToVector3(), targetBed.Position.ToVector3(), color);
            GenDraw.DrawFieldEdges(targetBed.GetRoom().Cells.ToList(), color.ToUnityColor());

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
    
        //public static MethodInfo PrisUtilHelper()
        //{
        //    var prisUtilapp = AppDomain.CurrentDomain.GetAssemblies().Where(x => x.FullName.Contains("PrisonerUtil")).First();
        //    if (prisUtilapp != null) {
        //        var assem = prisUtilapp.CreateInstance("InitialInteractionMode_Patches");
        //        var method = assem.GetType().GetMethod("AddHumanlikeOrders_Post");
        //        return method;
                    
        //    }
        //    return null;
            
        //}
    }


}
