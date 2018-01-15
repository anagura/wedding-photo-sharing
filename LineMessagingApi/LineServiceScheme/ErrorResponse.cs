using System.Runtime.Serialization;

namespace LineMessagingApi
{
    public class ErrorResponse
    {
        [DataMember(Name = "message")]
        public string Message { get; set; }

        [DataMember(Name = "details")]
        public Detail[] Details { get; set; }
    }

    public class Detail
    {
        [DataMember(Name = "message")]
        public string Message { get; set; }

        [DataMember(Name = "property")]
        public string Property { get; set; }
    }

}
