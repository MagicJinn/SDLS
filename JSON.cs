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
        private static JsonReader jsonReader;
        private static JsonWriter jsonWriter;
#endif //     Has no Skies equivelant

        public static void PrepareJsonManipulation()
        {
#if !SKIES // Sea build
            jsonReader = new JsonReader();
            jsonWriter = new JsonWriter();
#endif //     Has no Skies equivelant
        }

        public static string Serialize(object data)
        {
            string serializedData;
#if !SKIES // Sea build
            serializedData = jsonWriter.Write(data);
#else //      Skies build
            serializedData = JsonConvert.SerializeObject(data);
#endif
            return serializedData;
        }

        public static Dictionary<string, object> Deserialize(string jsonText)
        {
#if !SKIES // Sea build
            return jsonReader.Read<Dictionary<string, object>>(jsonText);
#else //      Skies build
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonText);
#endif
        }
    }
}
