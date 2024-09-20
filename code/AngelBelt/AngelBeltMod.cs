using System;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using System.Collections.Generic;


namespace AngelBelt
{
    public class AngelBeltMod : ModSystem
    {
        public KeyCombination flykeys = new KeyCombination
        {
            KeyCode = (int)GlKeys.Space,
            SecondKeyCode = (int)GlKeys.Space,
            Alt = false,
            Ctrl = false,
            Shift = false
        };
        public GlKeys flykey = GlKeys.R;

        private ICoreAPI api;

        private Item ItemEnabledFlight = null;
        private Item ItemDisabledFlight = null;

        private IClientNetworkChannel clientChannel;
        private IServerNetworkChannel serverChannel;

        private float SavedSpeedMult = 1f;
        private EnumFreeMovAxisLock SavedAxis = EnumFreeMovAxisLock.None;

        private List<string> _flyingPlayers = new List<string>();
        private long _eventID = 0;

        private bool _useCharge = false;

        // client stuff
        private ICoreClientAPI capi;        

        public bool IsShuttingDown { get; set; }

        // server stuff
        private ICoreServerAPI sapi;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterItemClass("AngelBelt", typeof(AngelBeltItem));
            this.api = api;            
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            this.capi = api;
            RegisterFlyKeys();

            clientChannel = api.Network.RegisterChannel("angelbelt").RegisterMessageType(typeof
                (BeltToggle)).RegisterMessageType(typeof(BeltResponse)).SetMessageHandler<BeltResponse>(new
                NetworkServerMessageHandler<BeltResponse>(this.OnClientReceived));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this.sapi = api;
            serverChannel = sapi.Network.RegisterChannel("angelbelt").RegisterMessageType(typeof
                (BeltToggle)).RegisterMessageType(typeof(BeltResponse)).SetMessageHandler<BeltToggle>(new
                NetworkClientMessageHandler<BeltToggle>(this.OnClientSent));
            sapi.Event.SaveGameLoaded += GetVariants;            
        }

        public override void Dispose()
        {
            if (_eventID != 0 && sapi != null) sapi.Event.UnregisterGameTickListener(_eventID);
            _eventID = 0;
        }

        private void GetVariants()
        {            
            AssetLocation onitemcode = AssetLocation.Create("angelbelt-on");
            AssetLocation offitemcode = AssetLocation.Create("angelbelt-off");

            ItemEnabledFlight = sapi.World.GetItem(onitemcode);
            ItemDisabledFlight = sapi.World.GetItem(offitemcode);
            if ((ItemEnabledFlight as AngelBeltItem).UseCharge)
            {
                _useCharge = true;
                _eventID = sapi.Event.RegisterGameTickListener(new Action<float>(OnSimTick), 1000, 1000);
            }
            sapi.World.Logger.StoryEvent(Lang.Get("Flying Free as a bird...", Array.Empty<object>()));
        }

        public void OnSimTick(float dt)
        {
            // this is only run on the server
            if (_flyingPlayers.Count == 0) return;
            bool validateplayers = false;
            List<string> invalidplayers = new List<string>();
            foreach (string playerid in _flyingPlayers)
            {
                IServerPlayer player = sapi.World.PlayerByUid(playerid) as IServerPlayer;
                if (player == null || player.ConnectionState != EnumClientState.Playing) 
                { 
                    validateplayers = true;
                    invalidplayers.Add(playerid);
                    continue; 
                }
                // we have a flying player to edit
                ItemStack belt = player.InventoryManager.GetOwnInventory("character")[(int)EnumCharacterDressType.Waist].Itemstack;
                if (belt != null && belt.Collectible is AngelBeltItem abi)
                {
                    int curcharge = belt.Collectible.GetRemainingDurability(belt);
                    if (curcharge == 1)
                    {
                        // disable belt
                        BeltToggle beltToggle = new BeltToggle()
                        {
                            toggle = playerid,
                            savedspeed = 1.0f,
                            savedaxis = "None"
                        };
                        Toggle(player, beltToggle);
                        api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/stonecrush"), player);
                        validateplayers = true;
                        invalidplayers.Add(playerid);
                        continue;
                    }
                    int movesquared = Math.Max((int)player.WorldData.MoveSpeedMultiplier, (int)player.WorldData.EntityControls.MovespeedMultiplier);
                    movesquared *= movesquared;
                    int movecost = abi.ChargePerSecond * movesquared;
                    if (curcharge < movecost)
                    {
                        // disable belt
                        BeltToggle beltToggle = new BeltToggle()
                        {
                            toggle = playerid,
                            savedspeed = 1.0f,
                            savedaxis = "None"
                        };
                        Toggle(player, beltToggle);
                        api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/stonecrush"), player);
                        validateplayers = true;
                        invalidplayers.Add(playerid);
                    }
                    else
                    {
                        curcharge -= movecost;
                        if (curcharge < 1) curcharge = 1;
                        belt.Attributes.SetInt("durability", curcharge);
                        player.InventoryManager.GetOwnInventory("character")[(int)EnumCharacterDressType.Waist].MarkDirty();
                    }
                }                
            }
            if (validateplayers)
            {
                foreach (string invalidplayer in invalidplayers)
                {
                    _flyingPlayers.Remove(invalidplayer);
                }
            }
        }

        // server recieves this from client
        private void OnClientSent(IPlayer fromPlayer, BeltToggle bt)
        {
            if (fromPlayer == null || bt == null)
                return;            
            // do stuff here BeltToggle object includes the player who sent the message
            bool successful = Toggle(fromPlayer, bt);
            BeltResponse bres = new BeltResponse();
            if (successful)
            {
                bres.response = "success";
                serverChannel.SendPacket<BeltResponse>(bres, fromPlayer as IServerPlayer);
            }
            else
            {
                bres.response = "fail";
                serverChannel.SendPacket<BeltResponse>(bres, fromPlayer as IServerPlayer);
            }
        }

        // server sends to given player
        private void OnClientReceived(BeltResponse response)
        {
            if (response.response == "success")
            {
                return;
                // do nothing, all is well with the world  
            }
            else if (response.response == "fail")
            {
                ItemStack belt = capi.World.Player.InventoryManager.GetOwnInventory("character")[(int)EnumCharacterDressType.Waist]?.Itemstack;
                if (belt != null && (belt.Collectible as AngelBeltItem).UseCharge)
                {
                    int charge = belt.Collectible.GetRemainingDurability(belt);
                    int movecost = ((int)capi.World.Player.Entity.Controls.MovespeedMultiplier);
                    movecost *= movecost;
                    movecost *= belt.Collectible.Attributes["chargepersecond"].AsInt(1);
                    if (charge <= 1 || charge < movecost)
                    {
                        capi.ShowChatMessage(Lang.Get("needscharging"));
                    }
                }
                else
                {
                    capi.ShowChatMessage("AngelBelt Toggle failed!");
                }
            }
            else
            {
                capi.ShowChatMessage("Angelbelt response unknown: " + response.response);
            }
        }

        // if we're in here, we're on the server
        public bool Toggle(IPlayer player, BeltToggle bt)
        {
            // get the waist slot
            ItemSlot waistSlot = player.InventoryManager.GetOwnInventory("character")[(int)EnumCharacterDressType.Waist];
            if (waistSlot == null)
            {
                Mod.Logger.VerboseDebug("AngelBelt: Waist Slot was null when trying to Toggle Flying...");
                return false;
            }

            if (bt.toggle == "updateflight")
            {
                // only change flight speed/axis, do not toggle on/off and only if the belt is on
                // client should not send packet if the belt is off or isn't present
                
                player.WorldData.MoveSpeedMultiplier = bt.savedspeed;
                player.WorldData.EntityControls.MovespeedMultiplier = bt.savedspeed;
                EnumFreeMovAxisLock axisLock = EnumFreeMovAxisLock.None;
                if (!Enum.TryParse<EnumFreeMovAxisLock>(bt.savedaxis, out axisLock))
                {
                    Mod.Logger.VerboseDebug("AngelBelt: Error Parsing Axis Log String. : " + bt.savedaxis);
                }
                player.WorldData.FreeMovePlaneLock = axisLock;
                player.WorldData.EntityControls.FlyPlaneLock = axisLock;
                ((IServerPlayer)player).BroadcastPlayerData();
                return true;
            }

            ItemStack swapStack;
            // we've already checked the players waist slot, so we know we're good.
            if (waistSlot.Itemstack.Item.LastCodePart() == "off")
            {
                // validate IF we have 'charge' left if charge is enabled...
                swapStack = new ItemStack(ItemEnabledFlight);
                
                if (_useCharge)
                {
                    int curcharge = waistSlot.Itemstack.Collectible.GetRemainingDurability(waistSlot.Itemstack);
                    int movecost = (int)bt.savedspeed * (int)bt.savedspeed;
                    movecost = movecost * swapStack.Collectible.Attributes["chargepersecond"].AsInt(1);
                    if (curcharge <= 1 || curcharge < movecost)
                    {
                        return false; 
                    }
                    swapStack.Attributes.SetInt("durability", curcharge);
                }
                // belt is off, we need to enable it.
                waistSlot.Itemstack = swapStack;
                waistSlot.MarkDirty();
                player.WorldData.FreeMove = true;
                player.Entity.Properties.FallDamageMultiplier = 0f;
                //player.Entity.Properties.FallDamage = false; value now obsolete
                api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), player);
                player.WorldData.MoveSpeedMultiplier = bt.savedspeed;
                player.WorldData.EntityControls.MovespeedMultiplier = bt.savedspeed;
                EnumFreeMovAxisLock axislock = EnumFreeMovAxisLock.None;
                if (!Enum.TryParse<EnumFreeMovAxisLock>(bt.savedaxis,out axislock))
                {
                    Mod.Logger.VerboseDebug("AngelBelt: Error Parsing Axis Lock string! : " + bt.savedaxis);
                }
                player.WorldData.FreeMovePlaneLock = axislock;
                if (_useCharge) _flyingPlayers.Add(player.PlayerUID);
                ((IServerPlayer)player).BroadcastPlayerData();

            }
            else
            {
                // belt is on, we need to disable it.
                swapStack = new ItemStack(ItemDisabledFlight);
                if (_useCharge)
                {
                    int curcharge = waistSlot.Itemstack.Collectible.GetRemainingDurability(waistSlot.Itemstack);
                    swapStack.Attributes.SetInt("durability", curcharge);
                }
                waistSlot.Itemstack = swapStack;
                waistSlot.MarkDirty();
                player.Entity.PositionBeforeFalling = player.Entity.Pos.XYZ;
                player.WorldData.FreeMove = false;
                //player.Entity.Properties.FallDamage = true; value now obsolete
                player.Entity.Properties.FallDamageMultiplier = 1.0f;
                api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), player);
                player.WorldData.MoveSpeedMultiplier = 1f;
                player.WorldData.EntityControls.MovespeedMultiplier = 1f;
                player.WorldData.FreeMovePlaneLock = EnumFreeMovAxisLock.None;
                if (_useCharge && _flyingPlayers.Contains(player.PlayerUID)) _flyingPlayers.Remove(player.PlayerUID);
                ((IServerPlayer)player).BroadcastPlayerData();
            }
                
            return true;
        }

        private bool OnFlyPlaneLock(KeyCombination comb)
        {
            if (CheckClientWaist())
            {                
                EnumFreeMovAxisLock newaxis = EnumFreeMovAxisLock.None;
                
                switch (capi.World.Player.WorldData.FreeMovePlaneLock)
                {
                    case EnumFreeMovAxisLock.None: newaxis = EnumFreeMovAxisLock.X; break;
                    case EnumFreeMovAxisLock.X: newaxis = EnumFreeMovAxisLock.Y; break;
                    case EnumFreeMovAxisLock.Y: newaxis = EnumFreeMovAxisLock.Z; break;
                    case EnumFreeMovAxisLock.Z: newaxis = EnumFreeMovAxisLock.None; break;
                    default: break;
                }
                this.SavedAxis = newaxis;
                capi.World.Player.WorldData.FreeMovePlaneLock = newaxis;
                BeltToggle beltToggle = new BeltToggle()
                {
                    toggle = "updateflight",
                    savedspeed = this.SavedSpeedMult,
                    savedaxis = this.SavedAxis.ToString()
                };
                clientChannel.SendPacket<BeltToggle>(beltToggle);

                capi.ShowChatMessage("Flight Plane Axis Locked To: " + this.SavedAxis.ToString());
                return true;
            }
            return false;
        }

        private bool OnFlySpeedInc(KeyCombination comb)
        {
            if (CheckClientWaist())
            {
                bool shiftused = comb.Alt;

                float curmult = capi.World.Player.WorldData.EntityControls.MovespeedMultiplier;

                if (!shiftused) curmult += 1f;
                else curmult += 0.1f;
                if (curmult == 10)
                {
                    capi.ShowChatMessage("Warning: High speed movement can strain servers.");
                }
                if (curmult == 20)
                {
                    capi.ShowChatMessage("Warp 2, Engage!");
                }
                //capi.World.Player.WorldData.MoveSpeedMultiplier = curmult;
                //capi.World.Player.WorldData.EntityControls.MovespeedMultiplier = curmult;
                this.SavedSpeedMult = curmult;
                BeltToggle beltToggle = new BeltToggle()
                {
                    toggle = "updateflight",
                    savedspeed = this.SavedSpeedMult,
                    savedaxis = this.SavedAxis.ToString()
                };
                clientChannel.SendPacket<BeltToggle>(beltToggle);
                capi.ShowChatMessage("Flight Speed Mult Now " + this.SavedSpeedMult.ToString());
                return true;
            }
            return false;
        }

        private bool OnFlySpeedDec(KeyCombination comb)
        {
            if (CheckClientWaist())
            {
                bool shiftused = comb.Alt;

                float curmult = capi.World.Player.WorldData.EntityControls.MovespeedMultiplier;
                if (curmult == 0)
                {
                    capi.ShowChatMessage("Flight Speed Multiplier cannot be negative.");
                    return false;
                }
                if (!shiftused) curmult -= 1f;
                else curmult -= 0.1f;
                if (curmult < 0) curmult = 0; // no more negative speeds
                //capi.World.Player.WorldData.MoveSpeedMultiplier = curmult;
                //capi.World.Player.WorldData.EntityControls.MovespeedMultiplier = curmult;
                this.SavedSpeedMult = curmult;
                BeltToggle beltToggle = new BeltToggle()
                {
                    toggle = "updateflight",
                    savedspeed = this.SavedSpeedMult,
                    savedaxis = this.SavedAxis.ToString()
                };
                clientChannel.SendPacket<BeltToggle>(beltToggle);
                capi.ShowChatMessage("Flight Speed Mult Now " + this.SavedSpeedMult.ToString());
                return true;
            }
            return false;
        }

        private bool OnFlySpeedReset(KeyCombination comb)
        {
            if (CheckClientWaist())
            {
                this.SavedSpeedMult = 1f;
                this.SavedAxis = EnumFreeMovAxisLock.None;
                BeltToggle beltToggle = new BeltToggle()
                {
                    toggle = "updateflight",
                    savedspeed = this.SavedSpeedMult,
                    savedaxis = this.SavedAxis.ToString()
                };
                clientChannel.SendPacket<BeltToggle>(beltToggle);

                //capi.World.Player.WorldData.MoveSpeedMultiplier = 1f;
                //capi.World.Player.WorldData.EntityControls.MovespeedMultiplier = 1f;
                //capi.World.Player.WorldData.FreeMovePlaneLock = EnumFreeMovAxisLock.None;
                capi.ShowChatMessage("Flight modifiers reset.");
                return true;
            }
            return false;
        }

        private bool OnFlyKeyPressed(KeyCombination comb)
        {
            if (api.Side != EnumAppSide.Client)
                return false;            
//            base.Mod.Logger.VerboseDebug("AngelBelt Fly Key Pressed");
            bool hasBelt = PlayerHasBelt();

            if (hasBelt)
            {
                BeltToggle beltToggle = new BeltToggle() {  toggle = capi.World.Player.PlayerUID,
                                                            savedspeed = this.SavedSpeedMult,
                                                            savedaxis = this.SavedAxis.ToString() };
                clientChannel.SendPacket<BeltToggle>(beltToggle);
                return true;
            }
            return false;
        }

        /// <summary>
        /// ClientSide ONLY:
        /// Returns true if player is wearing an AngelBelt and it is "on" (enabled)
        /// </summary>
        /// <returns>True if belt is on and active.</returns>
        private bool CheckClientWaist()
        {
            if (api.Side != EnumAppSide.Client)
                return false;

            bool hasBelt = PlayerHasBelt();
            if (hasBelt)
            {
                string lastCodepart = capi.World.Player.InventoryManager.GetOwnInventory("character")[(int)EnumCharacterDressType.Waist].Itemstack.Item.LastCodePart();
                if (lastCodepart.Contains("on"))
                {
                    return true;
                }
                else
                {
//                    Mod.Logger.VerboseDebug("AngelBelt: Belt not on, LastCodePart: " + lastCodepart);
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        /// <summary>
        /// Many hot keys to make life easier
        /// </summary>
        private void RegisterFlyKeys()
        {
            base.Mod.Logger.VerboseDebug("AngelBelt: flight hotkey handler for R");
            this.capi.Input.RegisterHotKey("angel", "Enable Angel Belt", GlKeys.R, HotkeyType.CharacterControls);
            this.capi.Input.SetHotKeyHandler("angel", OnFlyKeyPressed);

            this.capi.Input.RegisterHotKey("angelspinc", "Angel Belt: Fly Speed +1", GlKeys.PageUp, HotkeyType.CharacterControls, false, false, false);
            this.capi.Input.RegisterHotKey("angelspdec", "Angel Belt: Fly Speed -1", GlKeys.PageDown, HotkeyType.CharacterControls, false, false, false);
            this.capi.Input.RegisterHotKey("angelspres", "Angel Belt: Fly Speed Reset", GlKeys.Home, HotkeyType.CharacterControls, false, false, false);
            this.capi.Input.RegisterHotKey("angelflyplane", "Angel Belt: Fly Plane Lock", GlKeys.F3, HotkeyType.CharacterControls, false, false, false);

            this.capi.Input.RegisterHotKey("angelspincfrac", "Angel Belt: Fly Speed +0.1", GlKeys.PageUp, HotkeyType.CharacterControls, true, false, false);
            this.capi.Input.RegisterHotKey("angelspdecfrac", "Angel Belt: Fly Speed -0.1", GlKeys.PageDown, HotkeyType.CharacterControls, true, false, false);

            this.capi.Input.SetHotKeyHandler("angelspinc", OnFlySpeedInc);
            this.capi.Input.SetHotKeyHandler("angelspdec", OnFlySpeedDec);
            this.capi.Input.SetHotKeyHandler("angelspres", OnFlySpeedReset);
            this.capi.Input.SetHotKeyHandler("angelflyplane", OnFlyPlaneLock);
            this.capi.Input.SetHotKeyHandler("angelspincfrac", OnFlySpeedInc);
            this.capi.Input.SetHotKeyHandler("angelspdecfrac", OnFlySpeedDec);
        }

        /// <summary>
        /// Player has the AngelBelt in the Belt Slot
        /// </summary>
        /// <returns>true/false</returns>
        private bool PlayerHasBelt()
        {
            if (api.Side == EnumAppSide.Client)
            {
                if (capi.World == null)
                {
                    return false;
                }
                if (capi.World.Player == null)
                {
                    return false;
                }
                if (capi.World.Player.InventoryManager == null)
                {
                    return false;
                }

                IInventory ownInventory = this.capi.World.Player.InventoryManager.GetOwnInventory("character");
                if (ownInventory == null)
                {
                    return false;
                }
                ItemSlot beltslot = ownInventory[(int)EnumCharacterDressType.Waist];
                if (beltslot == null)
                {
                    return false;
                }
                if (beltslot.Empty)
                {
                    return false;
                }

                if (ownInventory[(int)EnumCharacterDressType.Waist].Itemstack.Item.FirstCodePart().Contains("angelbelt"))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
