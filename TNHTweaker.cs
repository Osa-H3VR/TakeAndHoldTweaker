﻿using System;
using BepInEx;
using UnityEngine;
using FistVR;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using System.IO;
using System.Collections;
using System.Linq;
using BepInEx.Logging;
using System.IO.IsolatedStorage;
using Valve.Newtonsoft.Json;
using Deli;

namespace FistVR
{
    [BepInPlugin("org.bebinex.plugins.tnhtweaker", "A plugin for tweaking tnh parameters", "0.1.3.1")]
    public class TNHTweaker : DeliMod
    {
        private static ConfigEntry<bool> printCharacters;
        private static ConfigEntry<bool> logPatrols;
        private static ConfigEntry<bool> logFileReads;
        private static ConfigEntry<bool> allowLog;
        private static ConfigEntry<bool> cacheCompatibleMagazines;

        private static string OutputFilePath;

        private static List<int> spawnedBossIndexes = new List<int>();

        private static bool filesBuilt = false;
        private static bool preventOutfitFunctionality = false;

        private void Awake()
        {
            Debug.Log("Hello World (from TNH Tweaker)");

            Harmony.CreateAndPatchAll(typeof(TNHTweaker));

            LoadConfigFile();

            SetupOutputDirectory();
        }

        private void LoadConfigFile()
        {
            Debug.Log("TNHTWEAKER -- GETTING CONFIG FILE");

            cacheCompatibleMagazines = BaseMod.Config.Bind("General",
                                    "CacheCompatibleMagazines",
                                    false,
                                    "If true, guns will be able to spawn with any compatible mag in TNH (Eg. by default the VSS cannot spawn with 30rnd magazines)");

            allowLog = BaseMod.Config.Bind("Debug",
                                    "EnableLogging",
                                    false,
                                    "Set to true to enable logging");

            printCharacters = BaseMod.Config.Bind("Debug",
                                         "PrintCharacterInfo",
                                         false,
                                         "Decide if should print all character info");

            logPatrols = BaseMod.Config.Bind("Debug",
                                    "LogPatrolSpawns",
                                    false,
                                    "If true, patrols that spawn will have log output");

            logFileReads = BaseMod.Config.Bind("Debug",
                                    "LogFileReads",
                                    false,
                                    "If true, reading from a file will log the reading process");

            //TNHTweakerLogger.LogGeneral = allowLog.Value;
            //TNHTweakerLogger.LogCharacter = printCharacters.Value;
            //TNHTweakerLogger.LogPatrol = logPatrols.Value;
            //TNHTweakerLogger.LogFile = logFileReads.Value;

            TNHTweakerLogger.LogGeneral = true;
            TNHTweakerLogger.LogCharacter = true;
            TNHTweakerLogger.LogPatrol = true;
            TNHTweakerLogger.LogFile = true;

        }

        private void SetupOutputDirectory()
        {
            OutputFilePath = Application.dataPath.Replace("/h3vr_Data", "/TNH_Tweaker");

            if (!Directory.Exists(OutputFilePath))
            {
                Directory.CreateDirectory(OutputFilePath);
            }
        }

        [HarmonyPatch(typeof(IM), "GenerateItemDBs")] // Specify target method with HarmonyPatch attribute
        [HarmonyPostfix]
        public static void DelayedItemInit()
        {
            //Debug.Log("TNHTweaker -- Performing delayed init for all loaded characters and sosigs");

        }


        [HarmonyPatch(typeof(TNH_UIManager), "Start")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool InitTNH(List<TNH_UIManager.CharacterCategory> ___Categories, TNH_CharacterDatabase ___CharDatabase)
        {
            GM.TNHOptions.Char = TNH_Char.DD_ClassicLoudoutLouis;

            //Perform first time setup of all files
            if (!filesBuilt)
            {
                TNHTweakerLogger.Log("TNHTweaker -- Performing TNH Initialization", TNHTweakerLogger.LogType.File);

                //Load all of the default templates into our dictionaries
                LoadDefaultSosigs();
                LoadDefaultCharacters(___CharDatabase.Characters);
                LoadedTemplateManager.DefaultIconSprites = TNHTweakerUtils.GetAllIcons(LoadedTemplateManager.DefaultCharacters);

                //Remove all objects that havn't been loaded
                foreach (SosigTemplate template in LoadedTemplateManager.CustomSosigs)
                {
                    TNHTweakerUtils.RemoveUnloadedObjectIDs(template);
                }

                foreach (CustomCharacter template in LoadedTemplateManager.CustomCharacters)
                {
                    TNHTweakerUtils.RemoveUnloadedObjectIDs(template.GetCharacter());
                }

                //Perform the delayed init for default characters
                foreach (CustomCharacter character in LoadedTemplateManager.DefaultCharacters)
                {
                    character.DelayedInit(false);
                }

                //Perform the delayed init for all custom loaded characters and sosigs
                foreach (CustomCharacter character in LoadedTemplateManager.CustomCharacters)
                {
                    character.DelayedInit(true);
                }
                foreach (SosigTemplate sosig in LoadedTemplateManager.CustomSosigs)
                {
                    sosig.DelayedInit();
                }

                //Create files relevant for character creation
                TNHTweakerUtils.CreateDefaultSosigTemplateFiles(LoadedTemplateManager.DefaultSosigs, OutputFilePath);
                TNHTweakerUtils.CreateDefaultCharacterFiles(LoadedTemplateManager.DefaultCharacters, OutputFilePath);
                TNHTweakerUtils.CreateIconIDFile(OutputFilePath, LoadedTemplateManager.DefaultIconSprites.Keys.ToList());
                TNHTweakerUtils.CreateObjectIDFile(OutputFilePath);
                TNHTweakerUtils.CreateSosigIDFile(OutputFilePath);
                
                if (cacheCompatibleMagazines.Value)
                {
                    TNHTweakerUtils.LoadMagazineCache(OutputFilePath);
                }
            }
            
            //Load all characters into the UI
            foreach (TNH_CharacterDef character in LoadedTemplateManager.LoadedCharactersDict.Keys)
            {
                if (!___Categories[(int)character.Group].Characters.Contains(character.CharacterID))
                {
                    ___Categories[(int)character.Group].Characters.Add(character.CharacterID);
                    ___CharDatabase.Characters.Add(character);
                }
            }

            filesBuilt = true;
            return true;
        }

        private static void LoadDefaultSosigs()
        {
            TNHTweakerLogger.Log("TNHTweaker -- Adding default sosigs", TNHTweakerLogger.LogType.File);

            //Now load all default sosig templates into custom sosig dictionary
            foreach (SosigEnemyTemplate sosig in ManagerSingleton<IM>.Instance.odicSosigObjsByID.Values)
            {
                LoadedTemplateManager.AddSosigTemplate(sosig);
            }
        }

        private static void LoadDefaultCharacters(List<TNH_CharacterDef> characters)
        {
            TNHTweakerLogger.Log("TNHTweaker -- Adding default characters", TNHTweakerLogger.LogType.File);

            foreach (TNH_CharacterDef character in characters)
            {
                LoadedTemplateManager.AddCharacterTemplate(character);
            }
        }


        [HarmonyPatch(typeof(TNH_Manager), "InitTables")] // Specify target method with HarmonyPatch attribute
        [HarmonyPostfix]
        public static void PrintGenerateTables(Dictionary<ObjectTableDef, ObjectTable> ___m_objectTableDics)
        {
            try
            {
                string path = OutputFilePath + "/pool_contents.txt";

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                // Create a new file     
                using (StreamWriter sw = File.CreateText(path))
                {
                    foreach (KeyValuePair<ObjectTableDef, ObjectTable> pool in ___m_objectTableDics)
                    {
                        sw.WriteLine("Pool: " + pool.Key.Icon.name);
                        foreach(FVRObject obj in pool.Value.Objs)
                        {
                            if(obj == null)
                            {
                                TNHTweakerLogger.Log("TNHTWEAKER -- NULL OBJECT IN TABLE", TNHTweakerLogger.LogType.Character);
                                continue;
                            }
                            sw.WriteLine("-" + obj.ItemID);
                        }
                        sw.WriteLine("");
                    }
                }
            }

            catch (Exception ex)
            {
                Debug.LogError(ex.ToString());
            }

        }

        private static int GetValidPatrolIndex(List<TNH_PatrolChallenge.Patrol> patrols)
        {
            int index = UnityEngine.Random.Range(0, patrols.Count);
            int attempts = 0;

            while(spawnedBossIndexes.Contains(index) && attempts < patrols.Count)
            {
                index += 1;
                if (index >= patrols.Count) index = 0;
            }

            if (spawnedBossIndexes.Contains(index)) return -1;

            return index;
        }

        [HarmonyPatch(typeof(TNH_Manager), "GenerateValidPatrol")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool GenerateValidPatrolReplacement(TNH_PatrolChallenge P, int curStandardIndex, int excludeHoldIndex, bool isStart, TNH_Manager __instance, TNH_Progression.Level ___m_curLevel, List<TNH_Manager.SosigPatrolSquad> ___m_patrolSquads, ref float ___m_timeTilPatrolCanSpawn)
        {
            TNHTweakerLogger.Log("TNHTWEAKER -- GENERATING A PATROL -- THERE ARE CURRENTLY " + ___m_patrolSquads.Count + " PATROLS ACTIVE", TNHTweakerLogger.LogType.Patrol);

            if (P.Patrols.Count < 1) return false;

            //Get a valid patrol index, and exit if there are no valid patrols
            int patrolIndex = GetValidPatrolIndex(P.Patrols);
            if(patrolIndex == -1)
            {
                TNHTweakerLogger.Log("TNHTWEAKER -- NO VALID PATROLS", TNHTweakerLogger.LogType.Patrol);
                ___m_timeTilPatrolCanSpawn = 999;
                return false;
            }

            TNHTweakerLogger.Log("TNHTWEAKER -- VALID PATROL FOUND", TNHTweakerLogger.LogType.Patrol);

            TNH_PatrolChallenge.Patrol patrol = P.Patrols[patrolIndex];

            List<int> validLocations = new List<int>();
            float minDist = __instance.TAHReticle.Range * 1.2f;

            //Get a safe starting point for the patrol to spawn
            TNH_SafePositionMatrix.PositionEntry startingEntry;
            if (isStart) startingEntry = __instance.SafePosMatrix.Entries_SupplyPoints[curStandardIndex];
            else startingEntry = __instance.SafePosMatrix.Entries_HoldPoints[curStandardIndex];


            for(int i = 0; i < startingEntry.SafePositions_HoldPoints.Count; i++)
            {
                if(i != excludeHoldIndex && startingEntry.SafePositions_HoldPoints[i])
                {
                    float playerDist = Vector3.Distance(GM.CurrentPlayerBody.transform.position, __instance.HoldPoints[i].transform.position);
                    if(playerDist > minDist)
                    {
                        validLocations.Add(i);
                    }
                }
            }

            if (validLocations.Count < 1) return false;
            validLocations.Shuffle();

            TNH_Manager.SosigPatrolSquad squad = GeneratePatrol(validLocations[0], __instance, ___m_curLevel, patrol, patrolIndex);

            if(__instance.EquipmentMode == TNHSetting_EquipmentMode.Spawnlocking)
            {
                ___m_timeTilPatrolCanSpawn = patrol.TimeTilRegen;
            }
            else
            {
                ___m_timeTilPatrolCanSpawn = patrol.TimeTilRegen_LimitedAmmo;
            }

            ___m_patrolSquads.Add(squad);

            return false;
        }

        
        public static TNH_Manager.SosigPatrolSquad GeneratePatrol(int HoldPointStart, TNH_Manager instance, TNH_Progression.Level level, TNH_PatrolChallenge.Patrol patrol, int patrolIndex)
        {
            TNH_Manager.SosigPatrolSquad squad = new TNH_Manager.SosigPatrolSquad();

            squad.PatrolPoints = new List<Vector3>();
            foreach(TNH_HoldPoint holdPoint in instance.HoldPoints)
            {
                squad.PatrolPoints.Add(holdPoint.SpawnPoints_Sosigs_Defense.GetRandom<Transform>().position);
            }

            Vector3 startingPoint = squad.PatrolPoints[HoldPointStart];
            squad.PatrolPoints.RemoveAt(HoldPointStart);
            squad.PatrolPoints.Insert(0, startingPoint);

            int PatrolSize = Mathf.Clamp(patrol.PatrolSize, 0, instance.HoldPoints[HoldPointStart].SpawnPoints_Sosigs_Defense.Count);

            CustomCharacter character = LoadedTemplateManager.LoadedCharactersDict[instance.C];
            Level currLevel = character.GetCurrentLevel(level);
            Patrol currPatrol = currLevel.GetPatrol(patrol);

            TNHTweakerLogger.Log("TNHTWEAKER -- IS PATROL BOSS: " + currPatrol.IsBoss, TNHTweakerLogger.LogType.Patrol);

            for (int i = 0; i < PatrolSize; i++)
            {
                SosigEnemyTemplate template;
                bool allowAllWeapons;

                //If this is a boss, then we can only spawn it once, so add it to the list of spawned bosses
                if (currPatrol.IsBoss)
                {
                    spawnedBossIndexes.Add(patrolIndex);
                }

                //Select a sosig template from the custom character patrol
                if (i == 0)
                {
                    template = ManagerSingleton<IM>.Instance.odicSosigObjsByID[(SosigEnemyID)LoadedTemplateManager.SosigIDDict[currPatrol.LeaderType]];
                    allowAllWeapons = true;
                }

                else
                {
                    template = ManagerSingleton<IM>.Instance.odicSosigObjsByID[(SosigEnemyID)LoadedTemplateManager.SosigIDDict[currPatrol.EnemyType.GetRandom<string>()]];
                    allowAllWeapons = false;
                }


                SosigTemplate customTemplate = LoadedTemplateManager.LoadedSosigsDict[template];
                FVRObject droppedObject = instance.Prefab_HealthPickupMinor;

                //If squad is set to swarm, the first point they path to should be the players current position
                Sosig sosig;
                if (currPatrol.SwarmPlayer)
                {
                    squad.PatrolPoints[0] = GM.CurrentPlayerBody.transform.position;
                    sosig = SpawnEnemy(customTemplate, character, instance.HoldPoints[HoldPointStart].SpawnPoints_Sosigs_Defense[i], instance.AI_Difficulty, currPatrol.IFFUsed, true, squad.PatrolPoints[0], allowAllWeapons);
                    sosig.SetAssaultSpeed(currPatrol.AssualtSpeed);
                }
                else
                {
                    sosig = SpawnEnemy(customTemplate, character, instance.HoldPoints[HoldPointStart].SpawnPoints_Sosigs_Defense[i], instance.AI_Difficulty, currPatrol.IFFUsed, true, squad.PatrolPoints[0], allowAllWeapons);
                    sosig.SetAssaultSpeed(currPatrol.AssualtSpeed);
                }

                //Handle patrols dropping health
                if(i == 0 && UnityEngine.Random.value < currPatrol.DropChance)
                {
                    sosig.Links[1].RegisterSpawnOnDestroy(droppedObject);
                }

                squad.Squad.Add(sosig);
            }


            return squad;
        }


        [HarmonyPatch(typeof(TNH_Manager), "SetPhase_Take")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static void BeforeSetTake(TNH_CharacterDef ___C)
        {
            spawnedBossIndexes.Clear();
            preventOutfitFunctionality = LoadedTemplateManager.LoadedCharactersDict[___C].ForceDisableOutfitFunctionality;
        }


        [HarmonyPatch(typeof(TNH_Manager), "SetPhase_Take")] // Specify target method with HarmonyPatch attribute
        [HarmonyPostfix]
        public static void AfterSetTake(List<TNH_SupplyPoint> ___SupplyPoints, TNH_Progression.Level ___m_curLevel, TAH_Reticle ___TAHReticle, int ___m_level, TNH_CharacterDef ___C)
        {
            TNHTweakerLogger.Log("TNHTWEAKER -- ADDING ADDITIONAL SUPPLY POINTS", TNHTweakerLogger.LogType.General);

            CustomCharacter character = LoadedTemplateManager.LoadedCharactersDict[___C];
            Level currLevel = character.GetCurrentLevel(___m_curLevel);

            List <TNH_SupplyPoint> possiblePoints = new List<TNH_SupplyPoint>(___SupplyPoints);
            possiblePoints.Remove(___SupplyPoints[GetClosestSupplyPointIndex(___SupplyPoints, GM.CurrentPlayerBody.Head.position)]);

            foreach(TNH_SupplyPoint point in ___SupplyPoints)
            {
                if((int)Traverse.Create(point).Field("m_activeSosigs").Property("Count").GetValue() > 0)
                {
                    TNHTweakerLogger.Log("TNHTWEAKER -- FOUND ALREADY POPULATED POINT", TNHTweakerLogger.LogType.General);
                    possiblePoints.Remove(point);
                }
            }

            possiblePoints.Shuffle();

            //Now that we have a list of valid points, set up some of those points
            for(int i = 0; i < currLevel.AdditionalSupplyPoints && i < possiblePoints.Count; i++)
            {
                TNH_SupplyPoint.SupplyPanelType panelType = (TNH_SupplyPoint.SupplyPanelType)UnityEngine.Random.Range(1,3);
                        
                possiblePoints[i].Configure(___m_curLevel.SupplyChallenge, true, true, true, panelType, 1, 2);
                TAH_ReticleContact contact = ___TAHReticle.RegisterTrackedObject(possiblePoints[i].SpawnPoint_PlayerSpawn, TAH_ReticleContact.ContactType.Supply);
                possiblePoints[i].SetContact(contact);

                TNHTweakerLogger.Log("TNHTWEAKER -- GENERATED AN ADDITIONAL SUPPLY POINT", TNHTweakerLogger.LogType.General);
            }
                
        }


        [HarmonyPatch(typeof(TNH_HoldPoint), "SpawnTakeEnemyGroup")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool SpawnTakeGroupReplacement(List<Transform> ___SpawnPoints_Sosigs_Defense, TNH_TakeChallenge ___T, TNH_Manager ___M, List<Sosig> ___m_activeSosigs)
        {
            ___SpawnPoints_Sosigs_Defense.Shuffle<Transform>();

            for(int i = 0; i < ___T.NumGuards && i < ___SpawnPoints_Sosigs_Defense.Count; i++)
            {
                Transform transform = ___SpawnPoints_Sosigs_Defense[i];
                Debug.Log("Take challenge sosig ID : " + ___T.GID);
                SosigEnemyTemplate template = ManagerSingleton<IM>.Instance.odicSosigObjsByID[___T.GID];
                SosigTemplate customTemplate = LoadedTemplateManager.LoadedSosigsDict[template];

                TNHTweakerLogger.Log("TNHTWEAKER -- SPAWNING TAKE GROUP AT " + transform.position, TNHTweakerLogger.LogType.Patrol);

                Sosig enemy = SpawnEnemy(customTemplate, LoadedTemplateManager.LoadedCharactersDict[___M.C], transform, ___M.AI_Difficulty, ___T.IFFUsed, false, transform.position, true);

                ___m_activeSosigs.Add(enemy);
            }

            return false;
        }



        [HarmonyPatch(typeof(TNH_HoldPoint), "SpawnTurrets")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool SpawnTurretsReplacement(List<Transform> ___SpawnPoints_Turrets, TNH_TakeChallenge ___T, TNH_Manager ___M, List<AutoMeater> ___m_activeTurrets)
        {
            ___SpawnPoints_Turrets.Shuffle<Transform>();
            FVRObject turretPrefab = ___M.GetTurretPrefab(___T.TurretType);

            for (int i = 0; i < ___T.NumTurrets && i < ___SpawnPoints_Turrets.Count; i++)
            {
                Vector3 pos = ___SpawnPoints_Turrets[i].position + Vector3.up * 0.25f;
                AutoMeater turret = Instantiate<GameObject>(turretPrefab.GetGameObject(), pos, ___SpawnPoints_Turrets[i].rotation).GetComponent<AutoMeater>();
                ___m_activeTurrets.Add(turret);
            }

            return false;
        }




        [HarmonyPatch(typeof(TNH_SupplyPoint), "SpawnTakeEnemyGroup")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool SpawnSupplyGroupReplacement(List<Transform> ___SpawnPoints_Sosigs_Defense, TNH_TakeChallenge ___T, TNH_Manager ___M, List<Sosig> ___m_activeSosigs)
        {
            ___SpawnPoints_Sosigs_Defense.Shuffle<Transform>();

            for (int i = 0; i < ___T.NumGuards && i < ___SpawnPoints_Sosigs_Defense.Count; i++)
            {
                Transform transform = ___SpawnPoints_Sosigs_Defense[i];
                SosigEnemyTemplate template = ManagerSingleton<IM>.Instance.odicSosigObjsByID[___T.GID];
                SosigTemplate customTemplate = LoadedTemplateManager.LoadedSosigsDict[template];

                TNHTweakerLogger.Log("TNHTWEAKER -- SPAWNING SUPPLY GROUP AT " + transform.position, TNHTweakerLogger.LogType.Patrol);

                Sosig enemy = SpawnEnemy(customTemplate, LoadedTemplateManager.LoadedCharactersDict[___M.C], transform, ___M.AI_Difficulty, ___T.IFFUsed, false, transform.position, true);

                ___m_activeSosigs.Add(enemy);
            }

            return false;
        }




        [HarmonyPatch(typeof(TNH_SupplyPoint), "SpawnDefenses")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool SpawnSupplyTurretsReplacement(List<Transform> ___SpawnPoints_Turrets, TNH_TakeChallenge ___T, TNH_Manager ___M, List<AutoMeater> ___m_activeTurrets)
        {
            ___SpawnPoints_Turrets.Shuffle<Transform>();
            FVRObject turretPrefab = ___M.GetTurretPrefab(___T.TurretType);

            for (int i = 0; i < ___T.NumTurrets && i < ___SpawnPoints_Turrets.Count; i++)
            {
                Vector3 pos = ___SpawnPoints_Turrets[i].position + Vector3.up * 0.25f;
                AutoMeater turret = Instantiate<GameObject>(turretPrefab.GetGameObject(), pos, ___SpawnPoints_Turrets[i].rotation).GetComponent<AutoMeater>();
                ___m_activeTurrets.Add(turret);
            }

            return false;
        }




        [HarmonyPatch(typeof(TNH_HoldPoint), "IdentifyEncryption")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool SpawnEncryptionReplacement(TNH_HoldPoint __instance, TNH_HoldChallenge.Phase ___m_curPhase)
        {
            if(___m_curPhase.MaxTargets <= 0)
            {
                Traverse.Create(__instance).Method("CompletePhase").GetValue();
                return false;
            }

            return true;
        }


        public static Sosig SpawnEnemy(SosigTemplate template, CustomCharacter character, Transform spawnLocation, TNHModifier_AIDifficulty difficulty, int IFF, bool isAssault, Vector3 pointOfInterest, bool allowAllWeapons)
        {
            if (character.ForceAllAgentWeapons) allowAllWeapons = true;

            TNHTweakerLogger.Log("TNHTWEAKER -- SPAWNING SOSIG: " + template.SosigEnemyID, TNHTweakerLogger.LogType.Patrol);

            //Create the sosig object
            GameObject sosigPrefab = Instantiate(IM.OD[template.SosigPrefabs.GetRandom<string>()].GetGameObject(), spawnLocation.position, spawnLocation.rotation);
            Sosig sosigComponent = sosigPrefab.GetComponentInChildren<Sosig>();

            //Fill out the sosigs config based on the difficulty
            SosigConfig config;

            if (difficulty == TNHModifier_AIDifficulty.Arcade) config = template.ConfigsEasy.GetRandom<SosigConfig>();
            else config = template.Configs.GetRandom<SosigConfig>();
            sosigComponent.Configure(config.GetConfigTemplate());
            sosigComponent.E.IFFCode = IFF;

            //Setup the sosigs inventory
            sosigComponent.Inventory.Init();
            sosigComponent.Inventory.FillAllAmmo();
            sosigComponent.InitHands();

            //Equip the sosigs weapons
            if(template.WeaponOptions.Count > 0)
            {
                GameObject weaponPrefab = IM.OD[template.WeaponOptions.GetRandom<string>()].GetGameObject();
                EquipSosigWeapon(sosigComponent, weaponPrefab, difficulty);
            }

            if (template.WeaponOptionsSecondary.Count > 0 && allowAllWeapons && template.SecondaryChance >= UnityEngine.Random.value)
            {
                GameObject weaponPrefab = IM.OD[template.WeaponOptionsSecondary.GetRandom<string>()].GetGameObject();
                EquipSosigWeapon(sosigComponent, weaponPrefab, difficulty);
            }

            if (template.WeaponOptionsTertiary.Count > 0 && allowAllWeapons && template.TertiaryChance >= UnityEngine.Random.value)
            {
                GameObject weaponPrefab = IM.OD[template.WeaponOptionsTertiary.GetRandom<string>()].GetGameObject();
                EquipSosigWeapon(sosigComponent, weaponPrefab, difficulty);
            }

            //Equip clothing to the sosig
            OutfitConfig outfitConfig = template.OutfitConfigs.GetRandom<OutfitConfig>();
            if(outfitConfig.Chance_Headwear >= UnityEngine.Random.value)
            {
                EquipSosigClothing(outfitConfig.Headwear, sosigComponent.Links[0], outfitConfig.ForceWearAllHead);
            }

            if (outfitConfig.Chance_Facewear >= UnityEngine.Random.value)
            {
                EquipSosigClothing(outfitConfig.Facewear, sosigComponent.Links[0], outfitConfig.ForceWearAllFace);
            }

            if (outfitConfig.Chance_Eyewear >= UnityEngine.Random.value)
            {
                EquipSosigClothing(outfitConfig.Eyewear, sosigComponent.Links[0], outfitConfig.ForceWearAllEye);
            }

            if (outfitConfig.Chance_Torsowear >= UnityEngine.Random.value)
            {
                EquipSosigClothing(outfitConfig.Torsowear, sosigComponent.Links[1], outfitConfig.ForceWearAllTorso);
            }

            if (outfitConfig.Chance_Pantswear >= UnityEngine.Random.value)
            {
                EquipSosigClothing(outfitConfig.Pantswear, sosigComponent.Links[2], outfitConfig.ForceWearAllPants);
            }

            if (outfitConfig.Chance_Pantswear_Lower >= UnityEngine.Random.value)
            {
                EquipSosigClothing(outfitConfig.Pantswear_Lower, sosigComponent.Links[3], outfitConfig.ForceWearAllPantsLower);
            }

            if (outfitConfig.Chance_Backpacks >= UnityEngine.Random.value)
            {
                EquipSosigClothing(outfitConfig.Backpacks, sosigComponent.Links[1], outfitConfig.ForceWearAllBackpacks);
            }

            //Setup link spawns
            if (config.GetConfigTemplate().UsesLinkSpawns)
            {
                for(int i = 0; i < sosigComponent.Links.Count; i++)
                {
                    if(config.GetConfigTemplate().LinkSpawnChance[i] >= UnityEngine.Random.value)
                    {
                        if(config.GetConfigTemplate().LinkSpawns.Count > i && config.GetConfigTemplate().LinkSpawns[i] != null && config.GetConfigTemplate().LinkSpawns[i].Category != FVRObject.ObjectCategory.Loot)
                        {
                            sosigComponent.Links[i].RegisterSpawnOnDestroy(config.GetConfigTemplate().LinkSpawns[i]);
                        }
                    }
                }
            }

            //Setup the sosigs orders
            if (isAssault)
            {
                sosigComponent.CurrentOrder = Sosig.SosigOrder.Assault;
                sosigComponent.FallbackOrder = Sosig.SosigOrder.Assault;
                sosigComponent.CommandAssaultPoint(pointOfInterest);
            }
            else
            {
                sosigComponent.CurrentOrder = Sosig.SosigOrder.Wander;
                sosigComponent.FallbackOrder = Sosig.SosigOrder.Wander;
                sosigComponent.CommandGuardPoint(pointOfInterest, true);
                sosigComponent.SetDominantGuardDirection(UnityEngine.Random.onUnitSphere);
            }
            sosigComponent.SetGuardInvestigateDistanceThreshold(25f);

            //Handle sosig dropping custom loot
            if (UnityEngine.Random.value < template.DroppedLootChance)
            {
                sosigComponent.Links[2].RegisterSpawnOnDestroy(template.TableDef.GetRandomObject());
            }

            return sosigComponent;
        }


        [HarmonyPatch(typeof(FVRPlayerBody), "SetOutfit")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool SetOutfitReplacement(SosigEnemyTemplate tem, PlayerSosigBody ___m_sosigPlayerBody)
        {
            if (___m_sosigPlayerBody == null) return false;

            GM.Options.ControlOptions.MBClothing = tem.SosigEnemyID;
            if(tem.SosigEnemyID != SosigEnemyID.None)
            {
                if(tem.OutfitConfig.Count > 0 && LoadedTemplateManager.LoadedSosigsDict.ContainsKey(tem))
                {
                    OutfitConfig outfitConfig = LoadedTemplateManager.LoadedSosigsDict[tem].OutfitConfigs.GetRandom();

                    List<GameObject> clothing = Traverse.Create(___m_sosigPlayerBody).Field("m_curClothes").GetValue<List<GameObject>>();
                    foreach (GameObject item in clothing)
                    {
                        Destroy(item);
                    }
                    clothing.Clear();

                    if (outfitConfig.Chance_Headwear >= UnityEngine.Random.value)
                    {
                        EquipSosigClothing(outfitConfig.Headwear, clothing, ___m_sosigPlayerBody.Sosig_Head, outfitConfig.ForceWearAllHead);
                    }

                    if (outfitConfig.Chance_Facewear >= UnityEngine.Random.value)
                    {
                        EquipSosigClothing(outfitConfig.Facewear, clothing, ___m_sosigPlayerBody.Sosig_Head, outfitConfig.ForceWearAllFace);
                    }

                    if (outfitConfig.Chance_Eyewear >= UnityEngine.Random.value)
                    {
                        EquipSosigClothing(outfitConfig.Eyewear, clothing, ___m_sosigPlayerBody.Sosig_Head, outfitConfig.ForceWearAllEye);
                    }

                    if (outfitConfig.Chance_Torsowear >= UnityEngine.Random.value)
                    {
                        EquipSosigClothing(outfitConfig.Torsowear, clothing, ___m_sosigPlayerBody.Sosig_Torso, outfitConfig.ForceWearAllTorso);
                    }

                    if (outfitConfig.Chance_Pantswear >= UnityEngine.Random.value)
                    {
                        EquipSosigClothing(outfitConfig.Pantswear, clothing, ___m_sosigPlayerBody.Sosig_Abdomen, outfitConfig.ForceWearAllPants);
                    }

                    if (outfitConfig.Chance_Pantswear_Lower >= UnityEngine.Random.value)
                    {
                        EquipSosigClothing(outfitConfig.Pantswear_Lower, clothing, ___m_sosigPlayerBody.Sosig_Legs, outfitConfig.ForceWearAllPantsLower);
                    }

                    if (outfitConfig.Chance_Backpacks >= UnityEngine.Random.value)
                    {
                        EquipSosigClothing(outfitConfig.Backpacks, clothing, ___m_sosigPlayerBody.Sosig_Torso, outfitConfig.ForceWearAllBackpacks);
                    }

                }
            }

            return false;
        }


        public static void EquipSosigWeapon(Sosig sosig, GameObject weaponPrefab, TNHModifier_AIDifficulty difficulty)
        {
            SosigWeapon weapon = Instantiate(weaponPrefab, sosig.transform.position + Vector3.up * 0.1f, sosig.transform.rotation).GetComponent<SosigWeapon>();
            weapon.SetAutoDestroy(true);
            weapon.O.SpawnLockable = false;

            TNHTweakerLogger.Log("TNHTWEAKER -- EQUIPPING WEAPON: " + weapon.gameObject.name, TNHTweakerLogger.LogType.Patrol);

            //Equip the sosig weapon to the sosig
            sosig.ForceEquip(weapon);
            weapon.SetAmmoClamping(true);
            if (difficulty == TNHModifier_AIDifficulty.Arcade) weapon.FlightVelocityMultiplier = 0.3f;
        }

        public static void EquipSosigClothing(List<string> options, SosigLink link, bool wearAll)
        {
            if (wearAll)
            {
                foreach(string clothing in options)
                {
                    GameObject clothingObject = Instantiate(IM.OD[clothing].GetGameObject(), link.transform.position, link.transform.rotation);
                    clothingObject.transform.SetParent(link.transform);
                    clothingObject.GetComponent<SosigWearable>().RegisterWearable(link);
                }
            }

            else
            {
                GameObject clothingObject = Instantiate(IM.OD[options.GetRandom<string>()].GetGameObject(), link.transform.position, link.transform.rotation);
                clothingObject.transform.SetParent(link.transform);
                clothingObject.GetComponent<SosigWearable>().RegisterWearable(link);
            }
        }


        public static void EquipSosigClothing(List<string> options, List<GameObject> playerClothing, Transform link,  bool wearAll)
        {
            if (wearAll)
            {
                foreach (string clothing in options)
                {
                    GameObject clothingObject = Instantiate(IM.OD[clothing].GetGameObject(), link.position, link.rotation);

                    Component[] children = clothingObject.GetComponentsInChildren<Component>(true);
                    foreach(Component child in children)
                    {
                        child.gameObject.layer = LayerMask.NameToLayer("ExternalCamOnly");

                        if(!(child is Transform) && !(child is MeshFilter) && !(child is MeshRenderer))
                        {
                            Destroy(child);
                        }
                    }

                    playerClothing.Add(clothingObject);
                    clothingObject.transform.SetParent(link);
                }
            }

            else
            {
                GameObject clothingObject = Instantiate(IM.OD[options.GetRandom<string>()].GetGameObject(), link.position, link.rotation);

                Component[] children = clothingObject.GetComponentsInChildren<Component>(true);
                foreach (Component child in children)
                {
                    child.gameObject.layer = LayerMask.NameToLayer("ExternalCamOnly");

                    if (!(child is Transform) && !(child is MeshFilter) && !(child is MeshRenderer))
                    {
                        Destroy(child);
                    }
                }

                playerClothing.Add(clothingObject);
                clothingObject.transform.SetParent(link);
            }
        }


        [HarmonyPatch(typeof(TNH_SupplyPoint), "SpawnBoxes")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool SpawnBoxesReplacement(TNH_SupplyPoint __instance, TNH_Manager ___M, List<GameObject> ___m_spawnBoxes)
        {

            CustomCharacter character = LoadedTemplateManager.LoadedCharactersDict[___M.C];
            Level currLevel = character.GetCurrentLevel((TNH_Progression.Level)Traverse.Create(___M).Field("m_curLevel").GetValue());

            __instance.SpawnPoints_Boxes.Shuffle();

            int boxesToSpawn = UnityEngine.Random.Range(currLevel.MinBoxesSpawned, currLevel.MaxBoxesSpawned + 1);

            TNHTweakerLogger.Log("TNHTWEAKER -- GOING TO SPAWN " + boxesToSpawn + " BOXES AT THIS SUPPLY POINT -- MIN (" + currLevel.MinBoxesSpawned + "), MAX (" + currLevel.MaxBoxesSpawned + ")", TNHTweakerLogger.LogType.General);

            for (int i = 0; i < boxesToSpawn; i++)
            {
                Transform spawnTransform = __instance.SpawnPoints_Boxes[UnityEngine.Random.Range(0, __instance.SpawnPoints_Boxes.Count)];
                Vector3 position = spawnTransform.position + Vector3.up * 0.1f + Vector3.right * UnityEngine.Random.Range(-0.5f, 0.5f) + Vector3.forward * UnityEngine.Random.Range(-0.5f, 0.5f);
                Quaternion rotation = Quaternion.Slerp(spawnTransform.rotation, UnityEngine.Random.rotation, 0.1f);
                GameObject box = Instantiate(___M.Prefabs_ShatterableCrates[UnityEngine.Random.Range(0, ___M.Prefabs_ShatterableCrates.Count)], position, rotation);
                ___m_spawnBoxes.Add(box);
                TNHTweakerLogger.Log("TNHTWEAKER -- BOX SPAWNED", TNHTweakerLogger.LogType.General);
            }

            int tokensSpawned = 0;

            foreach(GameObject boxObj in ___m_spawnBoxes)
            {
                if(tokensSpawned < currLevel.MinTokensPerSupply)
                {
                    boxObj.GetComponent<TNH_ShatterableCrate>().SetHoldingToken(___M);
                    tokensSpawned += 1;
                }

                else if (tokensSpawned < currLevel.MaxTokensPerSupply && UnityEngine.Random.value < currLevel.BoxTokenChance)
                {
                    boxObj.GetComponent<TNH_ShatterableCrate>().SetHoldingToken(___M);
                    tokensSpawned += 1;
                }

                else if (UnityEngine.Random.value < currLevel.BoxHealthChance)
                {
                    boxObj.GetComponent<TNH_ShatterableCrate>().SetHoldingHealth(___M);
                }
            }

            return false;
                
        }


        public static void SpawnGrenades(List<TNH_HoldPoint.AttackVector> AttackVectors, TNH_Manager M, int m_phaseIndex)
        {
            CustomCharacter character = LoadedTemplateManager.LoadedCharactersDict[M.C];
            Level currLevel = character.GetCurrentLevel((TNH_Progression.Level)Traverse.Create(M).Field("m_curLevel").GetValue());
            Phase currPhase = currLevel.HoldPhases[m_phaseIndex];

            float grenadeChance = currPhase.GrenadeChance;
            string grenadeType = currPhase.GrenadeType;
                        
            if(grenadeChance >= UnityEngine.Random.Range(0f, 1f))
            {
                TNHTweakerLogger.Log("TNHTWEAKER -- THROWING A GRENADE ", TNHTweakerLogger.LogType.General);

                //Get a random grenade vector to spawn a grenade at
                TNH_HoldPoint.AttackVector randAttackVector = AttackVectors[UnityEngine.Random.Range(0, AttackVectors.Count)];

                //Instantiate the grenade object
                GameObject grenadeObject = Instantiate(IM.OD[grenadeType].GetGameObject(), randAttackVector.GrenadeVector.position, randAttackVector.GrenadeVector.rotation);

                //Give the grenade an initial velocity based on the grenade vector
                grenadeObject.GetComponent<Rigidbody>().velocity = 15 * randAttackVector.GrenadeVector.forward;
                grenadeObject.GetComponent<SosigWeapon>().FuseGrenade();
            }
        }

        public static void SpawnHoldEnemyGroup(TNH_HoldChallenge.Phase curPhase, int phaseIndex, List<TNH_HoldPoint.AttackVector> AttackVectors, List<Transform> SpawnPoints_Turrets, List<Sosig> ActiveSosigs, TNH_Manager M, ref bool isFirstWave)
        {
            TNHTweakerLogger.Log("TNHTWEAKER -- SPAWNING AN ENEMY WAVE", TNHTweakerLogger.LogType.General);

            //TODO add custom property form MinDirections
            int numAttackVectors = UnityEngine.Random.Range(1, curPhase.MaxDirections + 1);
            numAttackVectors = Mathf.Clamp(numAttackVectors, 1, AttackVectors.Count);

            //Get the custom character data
            CustomCharacter character = LoadedTemplateManager.LoadedCharactersDict[M.C];
            Level currLevel = character.GetCurrentLevel((TNH_Progression.Level)Traverse.Create(M).Field("m_curLevel").GetValue());
            Phase currPhase = currLevel.HoldPhases[phaseIndex];

            //Set first enemy to be spawned as leader
            SosigEnemyTemplate enemyTemplate = ManagerSingleton<IM>.Instance.odicSosigObjsByID[(SosigEnemyID)LoadedTemplateManager.SosigIDDict[currPhase.LeaderType]];
            int enemiesToSpawn = UnityEngine.Random.Range(curPhase.MinEnemies, curPhase.MaxEnemies + 1);

            int sosigsSpawned = 0;
            int vectorSpawnPoint = 0;
            Vector3 targetVector;
            int vectorIndex = 0;
            while(sosigsSpawned < enemiesToSpawn)
            {
                TNHTweakerLogger.Log("TNHTWEAKER -- SPAWNING AT ATTACK VECTOR: " + vectorIndex, TNHTweakerLogger.LogType.General);

                if (AttackVectors[vectorIndex].SpawnPoints_Sosigs_Attack.Count <= vectorSpawnPoint) break;

                //Set the sosigs target position
                if (currPhase.SwarmPlayer)
                {
                    targetVector = GM.CurrentPlayerBody.TorsoTransform.position;
                }
                else
                {
                    targetVector = SpawnPoints_Turrets[UnityEngine.Random.Range(0, SpawnPoints_Turrets.Count)].position;
                }

                SosigTemplate customTemplate = LoadedTemplateManager.LoadedSosigsDict[enemyTemplate];

                Sosig enemy = SpawnEnemy(customTemplate, character, AttackVectors[vectorIndex].SpawnPoints_Sosigs_Attack[vectorSpawnPoint], M.AI_Difficulty, curPhase.IFFUsed, true, targetVector, true);

                ActiveSosigs.Add(enemy);

                TNHTweakerLogger.Log("TNHTWEAKER -- SOSIG SPAWNED", TNHTweakerLogger.LogType.General);

                //At this point, the leader has been spawned, so always set enemy to be regulars
                enemyTemplate = ManagerSingleton<IM>.Instance.odicSosigObjsByID[(SosigEnemyID)LoadedTemplateManager.SosigIDDict[currPhase.EnemyType.GetRandom<string>()]];
                sosigsSpawned += 1;

                vectorIndex += 1;
                if(vectorIndex >= numAttackVectors)
                {
                    vectorIndex = 0;
                    vectorSpawnPoint += 1;
                }

                
            }
            isFirstWave = false;

        }



        [HarmonyPatch(typeof(TNH_HoldPoint), "SpawningRoutineUpdate")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool SpawningUpdateReplacement(
            ref float ___m_tickDownToNextGroupSpawn,
            List<Sosig> ___m_activeSosigs,
            TNH_HoldPoint.HoldState ___m_state,
            ref bool ___m_hasThrownNadesInWave,
            List<TNH_HoldPoint.AttackVector> ___AttackVectors,
            List<Transform> ___SpawnPoints_Turrets,
            TNH_Manager ___M,
            TNH_HoldChallenge.Phase ___m_curPhase,
            int ___m_phaseIndex,
            ref bool ___m_isFirstWave)
        {

            ___m_tickDownToNextGroupSpawn -= Time.deltaTime;

            
            if (___m_activeSosigs.Count < 1)
            {
                if(___m_state == TNH_HoldPoint.HoldState.Analyzing)
                {
                    ___m_tickDownToNextGroupSpawn -= Time.deltaTime;
                }
            }

            if(!___m_hasThrownNadesInWave && ___m_tickDownToNextGroupSpawn <= 5f && !___m_isFirstWave)
            {
                SpawnGrenades(___AttackVectors, ___M, ___m_phaseIndex);
                ___m_hasThrownNadesInWave = true;
            }

            //Handle spawning of a wave if it is time
            if(___m_tickDownToNextGroupSpawn <= 0 && ___m_activeSosigs.Count + ___m_curPhase.MaxEnemies <= ___m_curPhase.MaxEnemiesAlive)
            {
                ___AttackVectors.Shuffle();

                SpawnHoldEnemyGroup(___m_curPhase, ___m_phaseIndex, ___AttackVectors, ___SpawnPoints_Turrets, ___m_activeSosigs, ___M, ref ___m_isFirstWave);
                ___m_hasThrownNadesInWave = false;
                ___m_tickDownToNextGroupSpawn = ___m_curPhase.SpawnCadence;
            }


            return false;
        }


        [HarmonyPatch(typeof(TNH_ObjectConstructor), "ButtonClicked")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool ButtonClickedReplacement(int i,
            TNH_ObjectConstructor __instance,
            EquipmentPoolDef ___m_pool,
            int ___m_curLevel,
            ref int ___m_selectedEntry,
            ref int ___m_numTokensSelected,
            bool ___allowEntry,
            List<EquipmentPoolDef.PoolEntry> ___m_poolEntries,
            List<int> ___m_poolAddedCost,
            GameObject ___m_spawnedCase)
        {
            Traverse constructorTraverse = Traverse.Create(__instance);

            constructorTraverse.Method("UpdateRerollButtonState", false).GetValue();

            if (!___allowEntry)
            {
                return false;
            }
            
            if(__instance.State == TNH_ObjectConstructor.ConstructorState.EntryList)
            {

                int cost = ___m_poolEntries[i].GetCost(__instance.M.EquipmentMode) + ___m_poolAddedCost[i];
                if(__instance.M.GetNumTokens() >= cost)
                {
                    constructorTraverse.Method("SetState", TNH_ObjectConstructor.ConstructorState.Confirm, i).GetValue();
                    SM.PlayCoreSound(FVRPooledAudioType.UIChirp, __instance.AudEvent_Select, __instance.transform.position);
                }
                else
                {
                    SM.PlayCoreSound(FVRPooledAudioType.UIChirp, __instance.AudEvent_Fail, __instance.transform.position);
                }
            }

            else if(__instance.State == TNH_ObjectConstructor.ConstructorState.Confirm)
            {

                if (i == 0)
                {
                    constructorTraverse.Method("SetState", TNH_ObjectConstructor.ConstructorState.EntryList, 0).GetValue();
                    ___m_selectedEntry = -1;
                    SM.PlayCoreSound(FVRPooledAudioType.UIChirp, __instance.AudEvent_Back, __instance.transform.position);
                }
                else if(i == 2)
                {
                    int cost = ___m_poolEntries[___m_selectedEntry].GetCost(__instance.M.EquipmentMode) + ___m_poolAddedCost[___m_selectedEntry];
                    if (__instance.M.GetNumTokens() >= cost)
                    {

                        if ((!___m_poolEntries[___m_selectedEntry].TableDef.SpawnsInSmallCase && !___m_poolEntries[___m_selectedEntry].TableDef.SpawnsInSmallCase) || ___m_spawnedCase == null)
                        {

                            AnvilManager.Run(SpawnObjectAtConstructor(___m_poolEntries[___m_selectedEntry], __instance, constructorTraverse));
                            ___m_numTokensSelected = 0;
                            __instance.M.SubtractTokens(cost);
                            SM.PlayCoreSound(FVRPooledAudioType.UIChirp, __instance.AudEvent_Spawn, __instance.transform.position);

                            if (__instance.M.C.UsesPurchasePriceIncrement)
                            {
                                ___m_poolAddedCost[___m_selectedEntry] += 1;
                            }

                            constructorTraverse.Method("SetState", TNH_ObjectConstructor.ConstructorState.EntryList, 0).GetValue();
                            ___m_selectedEntry = -1;
                        }

                        else
                        {
                            SM.PlayCoreSound(FVRPooledAudioType.UIChirp, __instance.AudEvent_Fail, __instance.transform.position);
                        }
                    }
                    else
                    {
                        SM.PlayCoreSound(FVRPooledAudioType.UIChirp, __instance.AudEvent_Fail, __instance.transform.position);
                    }
                }
            }

            return false;
        }


        private static IEnumerator SpawnObjectAtConstructor(EquipmentPoolDef.PoolEntry entry, TNH_ObjectConstructor constructor, Traverse constructorTraverse)
        {
            constructorTraverse.Field("allowEntry").SetValue(false);
            EquipmentPool pool = LoadedTemplateManager.EquipmentPoolDictionary[entry];
            CustomCharacter character = LoadedTemplateManager.LoadedCharactersDict[constructor.M.C];
            List<GameObject> trackedObjects = (List<GameObject>)(constructorTraverse.Field("m_trackedObjects").GetValue());

            if(pool.Tables[0].SpawnsInLargeCase || pool.Tables[0].SpawnsInSmallCase)
            {
                GameObject caseFab = constructor.M.Prefab_WeaponCaseLarge;
                if (pool.Tables[0].SpawnsInSmallCase) caseFab = constructor.M.Prefab_WeaponCaseSmall;

                FVRObject item = pool.Tables[0].GetObjectTable().GetRandomObject();
                GameObject itemCase = constructor.M.SpawnWeaponCase(caseFab, constructor.SpawnPoint_Case.position, constructor.SpawnPoint_Case.forward, item, pool.Tables[0].NumMagsSpawned, pool.Tables[0].NumRoundsSpawned, pool.Tables[0].MinAmmoCapacity, pool.Tables[0].MaxAmmoCapacity);

                constructorTraverse.Field("m_spawnedCase").SetValue(itemCase);
                itemCase.GetComponent<TNH_WeaponCrate>().M = constructor.M;
            }

            else
            {
                int mainSpawnCount = 0;
                int requiredSpawnCount = 0;
                int ammoSpawnCount = 0;
                int objectSpawnCount = 0;

                for (int tableIndex = 0; tableIndex < pool.Tables.Count; tableIndex++)
                {
                    ObjectPool table = pool.Tables[tableIndex];

                    for(int itemIndex = 0; itemIndex < table.ItemsToSpawn; itemIndex++)
                    {
                        FVRObject mainObject;

                        if (table.IsCompatibleMagazine)
                        {
                            mainObject = TNHTweakerUtils.GetMagazineForEquipped();
                            if(mainObject == null)
                            {
                                break;
                            }
                        }
                        else
                        {
                            mainObject = table.GetObjectTable().GetRandomObject();
                        }

                        Transform primarySpawn = constructor.SpawnPoint_Object;
                        Transform requiredSpawn = constructor.SpawnPoint_Object;
                        Transform ammoSpawn = constructor.SpawnPoint_Mag;

                        if (mainObject.Category == FVRObject.ObjectCategory.Firearm)
                        {
                            primarySpawn = constructor.SpawnPoints_GunsSize[mainObject.TagFirearmSize - FVRObject.OTagFirearmSize.Pocket];
                            requiredSpawn = constructor.SpawnPoint_Grenade;
                            mainSpawnCount += 1;
                        }
                        else if (mainObject.Category == FVRObject.ObjectCategory.Explosive || mainObject.Category == FVRObject.ObjectCategory.Thrown)
                        {
                            primarySpawn = constructor.SpawnPoint_Grenade;
                        }
                        else if (mainObject.Category == FVRObject.ObjectCategory.MeleeWeapon)
                        {
                            primarySpawn = constructor.SpawnPoint_Melee;
                        }

                        //Spawn the main object
                        yield return mainObject.GetGameObjectAsync();
                        GameObject spawnedObject = Instantiate(mainObject.GetGameObject(), primarySpawn.position + Vector3.up * 0.2f * mainSpawnCount, primarySpawn.rotation);
                        trackedObjects.Add(spawnedObject);

                        //Spawn any required objects
                        for (int j = 0; j < mainObject.RequiredSecondaryPieces.Count; j++)
                        {
                            yield return mainObject.RequiredSecondaryPieces[j].GetGameObjectAsync();
                            GameObject requiredItem = Instantiate(mainObject.RequiredSecondaryPieces[j].GetGameObject(), requiredSpawn.position + -requiredSpawn.right * 0.2f * requiredSpawnCount + Vector3.up * 0.2f * j, requiredSpawn.rotation);
                            trackedObjects.Add(requiredItem);
                            requiredSpawnCount += 1;
                        }

                        //If this object has compatible ammo object, then we should spawn those
                        FVRObject ammoObject = mainObject.GetRandomAmmoObject(mainObject, character.ValidAmmoEras, table.MinAmmoCapacity, table.MaxAmmoCapacity, character.ValidAmmoSets);
                        if (ammoObject != null)
                        {
                            int spawnCount = table.NumMagsSpawned;

                            if (ammoObject.Category == FVRObject.ObjectCategory.Cartridge)
                            {
                                ammoSpawn = constructor.SpawnPoint_Ammo;
                                spawnCount = table.NumRoundsSpawned;
                            }

                            yield return ammoObject.GetGameObjectAsync();

                            for (int j = 0; j < spawnCount; j++)
                            {
                                GameObject spawnedAmmo = Instantiate(ammoObject.GetGameObject(), ammoSpawn.position + -ammoSpawn.right * 0.15f * ammoSpawnCount + ammoSpawn.up * 0.15f * j, ammoSpawn.rotation);
                                trackedObjects.Add(spawnedAmmo);
                            }

                            ammoSpawnCount += 1;
                        }

                        //If this object equires picatinny sights, we should try to spawn one
                        if (mainObject.RequiresPicatinnySight && character.GetRequiredSightsTable() != null)
                        {
                            FVRObject sight = character.GetRequiredSightsTable().GetRandomObject();
                            yield return sight.GetGameObjectAsync();
                            GameObject spawnedSight = Instantiate(sight.GetGameObject(), constructor.SpawnPoint_Object.position + -constructor.SpawnPoint_Object.right * 0.15f * objectSpawnCount, constructor.SpawnPoint_Object.rotation);
                            trackedObjects.Add(spawnedSight);

                            for (int j = 0; j < sight.RequiredSecondaryPieces.Count; j++)
                            {
                                yield return sight.RequiredSecondaryPieces[j].GetGameObjectAsync();
                                GameObject spawnedRequired = Instantiate(sight.RequiredSecondaryPieces[j].GetGameObject(), constructor.SpawnPoint_Object.position + -constructor.SpawnPoint_Object.right * 0.15f * objectSpawnCount + Vector3.up * 0.15f * j, constructor.SpawnPoint_Object.rotation);
                                trackedObjects.Add(spawnedRequired);
                            }

                            objectSpawnCount += 1;
                        }

                        //If this object has bespoke attachments we'll try to spawn one
                        else if (mainObject.BespokeAttachments.Count > 0 && UnityEngine.Random.value < table.BespokeAttachmentChance)
                        {
                            FVRObject bespoke = mainObject.BespokeAttachments.GetRandom();
                            yield return bespoke.GetGameObjectAsync();
                            GameObject bespokeObject = Instantiate(bespoke.GetGameObject(), constructor.SpawnPoint_Object.position + -constructor.SpawnPoint_Object.right * 0.15f * objectSpawnCount, constructor.SpawnPoint_Object.rotation);
                            trackedObjects.Add(bespokeObject);

                            objectSpawnCount += 1;
                        }
                    }
                }
            }

            constructorTraverse.Field("allowEntry").SetValue(true);
            yield break;
        }


        [HarmonyPatch(typeof(TNH_SupplyPoint), "Configure")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool PrintSupplyPoint(TNH_SupplyPoint.SupplyPanelType panelType)
        {
            TNHTweakerLogger.Log("TNHTWEAKER -- CONFIGURING SUPPLY POINT -- PANEL TYPE: " + panelType.ToString(), TNHTweakerLogger.LogType.General);
            return true;
        }


        [HarmonyPatch(typeof(FVRObject), "GetRandomAmmoObject")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool PrintCompatableMagazines(FVRObject __instance)
        {
            TNHTweakerLogger.Log("TNHTWEAKER -- COMPATABLE MAGAZINE COUNT: " + __instance.CompatibleMagazines.Count, TNHTweakerLogger.LogType.General);
            foreach(FVRObject mag in __instance.CompatibleMagazines)
            {
                TNHTweakerLogger.Log(mag.ItemID, TNHTweakerLogger.LogType.General);
            }
            return true;
        }


        [HarmonyPatch(typeof(Sosig), "BuffHealing_Invis")] // Specify target method with HarmonyPatch attribute
        [HarmonyPrefix]
        public static bool OverrideCloaking()
        {
            return !preventOutfitFunctionality;
        }

        public static int GetClosestSupplyPointIndex(List<TNH_SupplyPoint> SupplyPoints, Vector3 playerPosition)
        {
            float minDist = 999999999f;
            int minIndex = 0;

            for (int i = 0; i < SupplyPoints.Count; i++)
            {
                float dist = Vector3.Distance(SupplyPoints[i].SpawnPoint_PlayerSpawn.position, playerPosition);
                if(dist < minDist)
                {
                    minDist = dist;
                    minIndex = i;
                }
            }

            return minIndex;
        }

    }
}
