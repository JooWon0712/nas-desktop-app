using System.Drawing;
using System.IO;

namespace NasDesk
{
    internal static class IconGenerator
    {
        private static void Main(string[] arguments)
        {
            using (Icon icon = NasTheme.CreateIcon())
            using (FileStream output = File.Create(arguments[0])) icon.Save(output);
        }
    }
}
