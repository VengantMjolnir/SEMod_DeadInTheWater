using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;

namespace DeadInTheWater
{
    public static class Config
    {
        public static bool ConfigExists(string filename, Type type, Logger log)
        {
            return MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, type);
        }

        public static T ReadFromFile<T>(string filename, Type type, Logger log)
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, type))
                    return default(T);

                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(filename, type))
                {
                    var file = reader.ReadToEnd();
                    return string.IsNullOrWhiteSpace(file) ? default(T) : MyAPIGateway.Utilities.SerializeFromXML<T>(file);
                }
            }
            catch (Exception e)
            {
                log?.Log($"Error reading the file '{filename}' from local storage\n{e.Message}\n\n{e.StackTrace}", MessageType.ERROR);
                return default(T);
            }
        }

        public static void WriteToFile<T>(string filename, Type type, T data, Logger log)
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, type))
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(filename, type);

                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(filename, type))
                {
                    var config = MyAPIGateway.Utilities.SerializeToXML(data);
                    writer.Write(config);
                }
            }
            catch (Exception e)
            {
                log?.Log($"Error writing the file '{filename}' in local storage\n{e.Message}\n\n{e.StackTrace}", MessageType.ERROR);
            }
        }
    }
}
