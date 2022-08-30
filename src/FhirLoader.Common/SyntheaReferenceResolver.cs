using Newtonsoft.Json.Linq;


namespace FhirLoader.Common
{
    ///<summary>
    /// Utility class for resolving Synthea bundle references
    ///</summary>
    public class SyntheaReferenceResolver
    {
        ///<summary>
        /// Resolves all UUIDs in Synthea bundle
        ///</summary>
        public static void ConvertUUIDs(JObject bundle)
        {
            ConvertUUIDs(bundle, CreateUUIDLookUpTable(bundle));
        }

        private static void ConvertUUIDs(JToken tok, Dictionary<string, IdTypePair> idLookupTable)
        {
            switch (tok.Type)
            {
                case JTokenType.Object:
                case JTokenType.Array:

                    foreach (var c in tok.Children())
                    {
                        ConvertUUIDs(c, idLookupTable);
                    }

                    return;
                case JTokenType.Property:
                    JProperty prop = (JProperty)tok;

                    if (prop.Value.Type == JTokenType.String &&
                        prop.Name == "reference" &&
                        idLookupTable.TryGetValue(prop.Value.ToString(), out var idTypePair))
                    {
                        prop.Value = idTypePair.ResourceType + "/" + idTypePair.Id;
                    }
                    else
                    {
                        ConvertUUIDs(prop.Value, idLookupTable);
                    }

                    return;
                case JTokenType.String:
                case JTokenType.Boolean:
                case JTokenType.Float:
                case JTokenType.Integer:
                case JTokenType.Date:
                    return;
                default:
                    throw new NotSupportedException($"Invalid token type {tok.Type} encountered");
            }
        }

        private static Dictionary<string, IdTypePair> CreateUUIDLookUpTable(JObject bundle)
        {
            Dictionary<string, IdTypePair> table = new Dictionary<string, IdTypePair>();
            JArray? entry = bundle["entry"] as JArray;

            if (entry == null)
            {
                throw new ArgumentException("Unable to find bundle entries for creating lookup table");
            }

            try
            {
                foreach (var resourceWrapper in entry)
                {
                    JToken? resource = resourceWrapper?["resource"];
                    string? fullUrl = resourceWrapper?["fullUrl"]?.ToString();
                    string? resourceType = resource?["resourceType"]?.ToString();
                    string? id = resource?["id"]?.ToString();

                    if (resourceWrapper is null || resource is null || fullUrl is null || resourceType is null || id is null)
                    {
                        throw new ArgumentException("Invalid resource found in bundle.");
                    }

                    table.Add(fullUrl, new IdTypePair(id, resourceType ));
                }
            }
            catch
            {
                Console.WriteLine("Error parsing resources in bundle");
                throw;
            }

            return table;
        }

        private class IdTypePair
        {
            public string Id { get; set; }

            public string ResourceType { get; set; }

            public IdTypePair(string id, string resourceType)
            {
                Id = id;
                ResourceType = resourceType;
            }
        }
    }
}