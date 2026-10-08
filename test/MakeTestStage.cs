// Makes a test custom stage package: a game stage's files converted to custom stage code C<nn>, zipped flat with a
// picture. Usage: MakeTestStage <game folder> <game stage code> <nn> <picture> <out.zip>   (with src\GameArt.cs, src\StagePack.cs)
using System;
using System.IO;
using System.IO.Compression;

namespace ExMoreStuff
{
    static class MakeTestStage
    {
        static void Main(string[] args)
        {
            string game = args[0], from = args[1], to = "C" + int.Parse(args[2]).ToString("D2"), picture = args[3], output = args[4];
            if (File.Exists(output)) File.Delete(output);
            using (ZipArchive zip = ZipFile.Open(output, ZipArchiveMode.Create))
            {
                foreach (string suffix in StagePack.Suffixes)
                {
                    byte[] data = StagePack.Recode(File.ReadAllBytes(Path.Combine(game, "resource", StagePack.RelativePath(from, suffix))), from, to);
                    using (Stream entry = zip.CreateEntry("STG_" + to + suffix).Open()) entry.Write(data, 0, data.Length);
                }
                zip.CreateEntryFromFile(picture, "STG_" + to + Path.GetExtension(picture));
            }
            Console.WriteLine(output + ": " + new FileInfo(output).Length + " bytes");
        }
    }
}
