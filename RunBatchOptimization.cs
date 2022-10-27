using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VMS.TPS.Common.Model.API;

namespace VMS.TPS
{
    public class Script
    {
        public Script()
        {
        }
        [MTAThread]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            string locPath = Path.GetDirectoryName(GetPath());
            Process proc = new Process();
            proc.StartInfo.FileName = Path.Combine(locPath, "BatchOptimization.exe");
            proc.StartInfo.Arguments = "";
            proc.StartInfo.UseShellExecute = true;
            //proc.StartInfo.RedirectStandardError = true;
            //proc.StartInfo.RedirectStandardOutput = true;
            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
        static public string GetPath([CallerFilePath] string fileName = null)
        {
            return fileName;
        }
    }
}