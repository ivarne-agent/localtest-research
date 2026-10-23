using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Newtonsoft.Json;

namespace Altinn.App.Models
{
    /// <summary>The form's data model (the fixture's data type <c>model</c>).</summary>
    [XmlRoot(ElementName = "model")]
    public class Model
    {
        /// <summary>The applicant's name.</summary>
        [XmlElement("name", Order = 1)]
        [JsonProperty("name")]
        [JsonPropertyName("name")]
        public string name { get; set; }

        /// <summary>A free-text message.</summary>
        [XmlElement("message", Order = 2)]
        [JsonProperty("message")]
        [JsonPropertyName("message")]
        public string message { get; set; }
    }
}
