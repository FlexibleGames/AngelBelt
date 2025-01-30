using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AngelBelt
{
    class AngelBeltItem : ItemWearable
    {
        //public bool UseCharge => Attributes != null ? base.Attributes["usecharge"].AsBool(false) : false;
        //public int ChargePerSecond => Attributes != null ? base.Attributes["chargepersecond"].AsInt(1) : 1;

        // I really don't need this object anymore, Tyron pushed all the code into ItemWearable which is awesome.

        // called when object is loaded
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }        

    }
}
