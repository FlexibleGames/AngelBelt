using System;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;


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

        private void GetVariants()
        {            
            AssetLocation onitemcode = AssetLocation.Create("angelbelt-on");
            AssetLocation offitemcode = AssetLocation.Create("angelbelt-off");

            ItemEnabledFlight = sapi.World.GetItem(onitemcode);
            ItemDisabledFlight = sapi.World.GetItem(offitemcode);
            sapi.World.Logger.StoryEvent(Lang.Get("Flying Free as a bird...", Array.Empty<object>()));
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
                capi.ShowChatMessage("AngelBelt Toggle failed!");
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
                ((IServerPlayer)player).BroadcastPlayerData();
                return true;
            }

            ItemStack swapStack;
            // we've already checked the players waist slot, so we know we're good.
            if (waistSlot.Itemstack.Item.LastCodePart() == "off")
            {
                // belt is off, we need to enable it.
                swapStack = new ItemStack(ItemEnabledFlight);
                waistSlot.Itemstack = swapStack;
                waistSlot.MarkDirty();
                player.WorldData.FreeMove = true;
                player.Entity.Properties.FallDamage = false;
                api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), player);
                player.WorldData.MoveSpeedMultiplier = bt.savedspeed;
                player.WorldData.EntityControls.MovespeedMultiplier = bt.savedspeed;
                EnumFreeMovAxisLock axislock = EnumFreeMovAxisLock.None;
                if (!Enum.TryParse<EnumFreeMovAxisLock>(bt.savedaxis,out axislock))
                {
                    Mod.Logger.VerboseDebug("AngelBelt: Error Parsing Axis Lock string! : " + bt.savedaxis);
                }
                player.WorldData.FreeMovePlaneLock = axislock;
                ((IServerPlayer)player).BroadcastPlayerData();

            }
            else
            {
                // belt is on, we need to disable it.
                swapStack = new ItemStack(ItemDisabledFlight);
                waistSlot.Itemstack = swapStack;
                waistSlot.MarkDirty();
                player.Entity.PositionBeforeFalling = player.Entity.Pos.XYZ;
                player.WorldData.FreeMove = false;
                player.Entity.Properties.FallDamage = true;
                api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), player);
                player.WorldData.MoveSpeedMultiplier = 1f;
                player.WorldData.EntityControls.MovespeedMultiplier = 1f;
                player.WorldData.FreeMovePlaneLock = EnumFreeMovAxisLock.None;
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
                //capi.World.Player.WorldData.FreeMovePlaneLock = newaxis;                
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
