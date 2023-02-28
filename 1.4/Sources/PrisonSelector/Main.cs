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
            }))
            {    
                var optsList= new List<FloatMenuOption>();
                var targetPawn = thing as Pawn;
                if (targetPawn.Spawned)
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
                    var menu = new FloatSubMenu("Take "+targetPawn.Name.ToString().CapitalizeFirst()+" To", tmp);
                    opts.Add(menu);
                    
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

        public static List<FloatMenuOption> getListOfPlaces(Pawn target, Pawn pawn)
        {
            var subOpts = new List<FloatMenuOption>();
            var roomArr= new List<Room>();
            var jobType = new JobDef();

            bool ofPlayer = target.IsColonistPlayerControlled;

            MapRoomInMap(Find.CurrentMap);

            if (target.Downed || target.IsPrisoner)
            {
                if (ofPlayer)
                {
                    roomArr = mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.Hospital).ToList();
                    jobType = JobDefOf.Rescue;
                }
                else
                {
                    roomArr = mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.PrisonCell || p.Role == RoomRoleDefOf.PrisonBarracks).ToList();
                    jobType = JobDefOf.Capture;
                }
            }
            short Index = 1;
            foreach (var room in roomArr)
            {
                if (room.ContainedBeds.Any(r => !r.AnyOccupants))
                {
                    var bed = room.ContainedBeds.Where(r => !r.AnyOccupants).First();
                    Job job = JobMaker.MakeJob(jobType, target, bed);
                    job.count = 1;
                    string label = string.Format("{0} {1}\n Take To {2} #{3}",
                                                 ofPlayer ? "Rescue" : "Capture",
                                                 target.Name.ToString(),
                                                 ofPlayer ? "Hospital" : "Prison",
                                                 Index.ToString());
                    

                    subOpts.Add(new FloatMenuOption(label, delegate() {
                        pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.MiscWork));
                    }));
                }

                Index++;
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
    }

}
