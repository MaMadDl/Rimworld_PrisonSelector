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
using Verse.Profile;
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
	static class FloatMenuMakerMap_AddJobGiverWorkOrders_Patch
	{
 
        public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
        {

            foreach(Thing thing in GenUI.ThingsUnderMouse(clickPos,1f,new TargetingParameters { 
                canTargetPawns = true,
            }))
            {    
                string menuText = "Take Pawn To";
                var optsList= new List<FloatMenuOption>();
                var targetPawn = thing as Pawn;
                if (targetPawn.Spawned)
                {   
                    if (targetPawn.Downed || targetPawn.IsPrisoner)
                    {
                        if (pawn.CanReach(targetPawn, PathEndMode.ClosestTouch, Danger.Deadly))
                        {
                            optsList = RoomMapper.getListOfPlaces(targetPawn
                                                                        , targetPawn.Faction == Faction.OfPlayer
                                                                        , pawn);
                        }
                    }
                }
                if (optsList.Count() != 0)
                {
                    var menu = new FloatSubMenu(menuText, optsList);
                    opts.Add(menu);
                    optsList.Clear();
                }
            }
        }
    }
    public class RoomMapper
    {
        public static Dictionary<Room, RoomRoleDef> mapRooms;

        public static List<FloatMenuOption> getListOfPlaces(Pawn target, bool factionOfPlayer, Pawn pawn)
        {
            var subOpts = new List<FloatMenuOption>();
            var roomArr= new List<Room>();
            var jobType = new JobDef();

            

            if (target.Downed || target.IsPrisoner)
            {
                if (factionOfPlayer)
                {
                    roomArr = mapRooms.Keys.Where(p => p.Role == RoomRoleDefOf.Hospital).ToList();
                    jobType = JobDefOf.Rescue;
                }
                else
                {
                    roomArr = mapRooms.Keys.Where(p => p.role == RoomRoleDefOf.PrisonCell || p.Role == RoomRoleDefOf.PrisonBarracks).ToList();
                    jobType = JobDefOf.Capture;
                }
            }
            foreach (var room in roomArr)
            {
                if (room.ContainedBeds.Any(r => !r.AnyOccupants))
                {
                    var bed = room.ContainedBeds.Where(r => !r.AnyOccupants).RandomElement();
                    Job job = JobMaker.MakeJob(jobType, target, bed);
                    string label = factionOfPlayer?"Rescue ":"Capture " + target.Name.ToString();
                    subOpts.Add(new FloatMenuOption(label, delegate() {
                        pawn.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc));
                    }));
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
    }

}
