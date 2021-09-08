using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace DeadInTheWater
{
    public enum DITW_State
    {
        WaitingForCockpits,
        Normal,
        DamagedAndChecking,
        DeadOrDying,
    }

    public class DITW_Config
    {
        public int idle_delay;

        public static DITW_Config Static { get; set; }
    }

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class DITW_Mod : MySessionComponentBase
    {
        public DITW_Mod Static { get; private set; }

        public Logger Logger;

        private StringBuilder _debug = new StringBuilder();
        private bool _isSetup = false;

        private void Init()
        {
            _isSetup = true;

            _debug.Clear();
            var prefix = MyAPIGateway.Session?.Name ?? "";
            foreach (var ch in prefix)
            {
                if (char.IsLetterOrDigit(ch))
                    _debug.Append(ch);
            }

            prefix = _debug.Length > 0 ? _debug.ToString() : "NewWorld";

            Logger.AddLine($"BeforeStart() - Checking Config");
            string configFilename = $"{prefix}_DITW.cfg";
            bool configExists = Config.ConfigExists(configFilename, typeof(DITW_Config), Logger);

            Logger.AddLine($"Config exists ({configFilename})? {configExists}");

            DITW_Config config;
            if (configExists)
            {
                config = Config.ReadFromFile<DITW_Config>(configFilename, typeof(DITW_Config), Logger);
            }
            else
            {
                config = new DITW_Config() { idle_delay = 12 };
                Config.WriteToFile<DITW_Config>(configFilename, typeof(DITW_Config), config, Logger);
            }
            DITW_Config.Static = config;

            Logger.AddLine($"\nFinished with settings\n\n");
            Logger.LogAll();
        }

        public override void BeforeStart()
        {
            base.BeforeStart();

            Static = this;

            try
            {
                MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
                Logger = new Logger("DeadInTheWater.log", MyAPIGateway.Utilities.IsDedicated);

                if (!_isSetup)
                {
                    Init();
                }
            }
            catch (Exception e)
            {
                Logger?.Log($"Error in BeforeStart:\n{e.Message}\n{e.StackTrace}", MessageType.ERROR);
                throw;
            }
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;

            Logger?.Close();

            base.UnloadData();
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
        }

    }

    public class Utilities
    {
        public static void Echo(string source, string message)
        {
            MyAPIGateway.Utilities.ShowMessage(source, message);
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true, new string [] { })]
    public class DITW_GridLogic : MyGameLogicComponent
    {
        private static readonly int TICK_COUNT_NORMAL = 12;
        private static readonly int TICK_COUNT_DAMAGED = 12;
        private static readonly int TICK_COUNT_DEAD = 3;

        private static readonly int DAMAGED_GRACE_PERIOD = 10;

        private DITW_State _controlState = DITW_State.Normal;
        private IMyCubeGrid _cubeGrid;

        private bool _init = false;
        private int _tickCounter = 0;
        private int _tickCountTarget = TICK_COUNT_NORMAL;
        private int _runCounter = 0;
        private int _blockDeathCount = 1;

        private Random _random = new Random();

        private struct CockpitCountResults
        {
            public int CockpitCount;
            public int FunctionalCockpitCount;
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _cubeGrid = Entity as IMyCubeGrid;

            Container.Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;

            base.Init(objectBuilder);
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();


            _tickCounter += 1;

            if (_tickCounter < _tickCountTarget)
            {
                return;
            }

            if (_cubeGrid.IsStatic)
            {
                // TODO - Make this configurable
                return;
            }

            CockpitCountResults results;
            if (_controlState == DITW_State.WaitingForCockpits)
            {
                results = CountFunctionalCockpits();
                if (results.CockpitCount > 0)
                {
                    ReturnToNormal();
                }
            }

            _tickCounter = 0;
            switch (_controlState)
            {
                case DITW_State.Normal:
                    results = CountFunctionalCockpits();
                    if (results.FunctionalCockpitCount == 0)
                    {
                        StartDamagedState();
                    }
                    break;
                case DITW_State.DamagedAndChecking:
                    results = CountFunctionalCockpits();
                    if (results.FunctionalCockpitCount > 0)
                    {
                        ReturnToNormal();
                    }
                    else if (results.CockpitCount == 0)
                    {
                        StartDying();
                    }
                    else
                    {
                        _runCounter++;
                        if (_runCounter > DAMAGED_GRACE_PERIOD)
                        {
                            StartDying();
                        }
                    }
                    break;
                case DITW_State.DeadOrDying:
                    {
                        DieALittleBitMore();
                    }
                    break;
            }
        }

        private void ReturnToNormal()
        {
            _controlState = DITW_State.Normal;
            _tickCountTarget = TICK_COUNT_NORMAL;

            _runCounter = 0;
            _blockDeathCount = 1;
        }

        private void StartDamagedState()
        {
            Utilities.Echo("DeadInTheWater", "Grid has no functional cockpits left. Death is imminent!");
            _controlState = DITW_State.DamagedAndChecking;
            _tickCountTarget = TICK_COUNT_DAMAGED;
        }

        private void StartDying()
        {
            Utilities.Echo("DeadInTheWater", "Grid is now dead!");
            _controlState = DITW_State.DeadOrDying;
            _tickCountTarget = TICK_COUNT_DEAD;
        }

        private void DieALittleBitMore()
        {
            if (MyAPIGateway.Session.IsServer)
            {
                List<IMySlimBlock> slimList = new List<IMySlimBlock>();
                _cubeGrid.GetBlocks(slimList, b =>
                {
                    return b.FatBlock != null && (b.FatBlock is IMyFunctionalBlock);
                });

                if (slimList.Count == 0)
                {
                    // We are all dead. Now we can just sit around waiting for a cockpit to be attached
                    _controlState = DITW_State.WaitingForCockpits;
                    return;
                }

                for (int i = 0; i < _blockDeathCount; ++i)
                {
                    // Destroy a random block, this is probably ok if it kills a block twice. 
                    // We don't need to be too precise
                    int index = _random.Next(0, slimList.Count - 1);
                    var block = slimList[index];
                    block.DoDamage(block.MaxIntegrity, MyStringHash.GetOrCompute("IgnoreShields"), true);
                }
                _blockDeathCount++;
            }
        }

        private CockpitCountResults CountFunctionalCockpits()
        {
            List<IMySlimBlock> slimList = new List<IMySlimBlock>();

            _cubeGrid.GetBlocks(slimList,
                block =>
                {
                    if (block.FatBlock == null || 
                        !(block.FatBlock is IMyCockpit || block.FatBlock is IMyRemoteControl))
                    {
                        return false;
                    }

                    return true;
                }
            );

            CockpitCountResults results = new CockpitCountResults() { CockpitCount = slimList.Count };
            foreach (var slim in slimList)
            {
                if (slim.FatBlock.IsFunctional)
                {
                    results.FunctionalCockpitCount++;
                }
            }

            return results;
        }
    }
}
