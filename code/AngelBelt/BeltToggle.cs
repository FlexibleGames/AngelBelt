using ProtoBuf;

namespace AngelBelt
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class BeltToggle
    {
        public string toggle;
        public float savedspeed;
        public string savedaxis;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class BeltResponse
    {
        public string response;
        public int charge;
    }
}
