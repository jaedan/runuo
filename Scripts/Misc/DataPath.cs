#region References
using System;
using System.IO;
using System.Linq;
#endregion

namespace Server.Misc
{
    public class DataPath
    {
        private static string m_Path;

        public static string FilePath
        {
            get
            {
                if (m_Path != null)
                    return m_Path;

                string path = Path.Combine(Core.BaseDirectory, "Data/datapath.cfg");

                if (!File.Exists(path))
                    return m_Path;

                foreach (var line in File.ReadLines(path).Select(o => o.Trim()))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    if (Directory.Exists(line))
                        return m_Path = line;
                }

                return m_Path;
            }
        }

        [CallPriority(int.MinValue + 1)]
        public static void Configure()
        {
            if (FilePath != null)
                Core.DataDirectories.Add(FilePath);
            else if (!Core.Service)
            {
                do
                {
                    Console.WriteLine("Enter the Ultima Online directory:");
                    Console.Write("> ");

                    m_Path = Console.ReadLine();

                    if (m_Path != null)
                        m_Path = m_Path.Trim();
                }
                while (m_Path == null || !Directory.Exists(m_Path));

                Core.DataDirectories.Add(m_Path);
            }

            Utility.PushColor(ConsoleColor.DarkYellow);
            Console.WriteLine("DataPath: " + string.Join("\n", Core.DataDirectories));
            Utility.PopColor();
        }
    }
}