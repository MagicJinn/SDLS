#if !SKIES // Sea build
using JsonFx.Json;
#else //      Has no Skies equivelant
using Newtonsoft.Json;
#endif

using System.Collections.Generic;

namespace SDLS
{
        public static class JSON
        {
#if !SKIES // Sea build
                private static JsonReader JSONReader;
                private static JsonWriter JSONWriter;
#endif //     Has no Skies equivelant

                public static void PrepareJSONManipulation()
                {
#if !SKIES // Sea build
                        JSONReader = new JsonReader();
                        JSONWriter = new JsonWriter();
#endif //     Has no Skies equivelant
                }

                public static string Serialize(object data)
                {
                        string serializedData;
#if !SKIES // Sea build
                        serializedData = JSONWriter.Write(data);
#else //      Skies build
            serializedData = JSONConvert.SerializeObject(data);
#endif
                        return serializedData;
                }

                public static Dictionary<string, object> Deserialize(string JSONText)
                {
#if !SKIES // Sea build
                        return JSONReader.Read<Dictionary<string, object>>(JSONText);
#else //      Skies build
            return JSONConvert.DeserializeObject<Dictionary<string, object>>(JSONText);
#endif
                }
        }
}
