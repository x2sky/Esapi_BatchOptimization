//////////////////////////////////////////////////////////////////////
///Class and functions to write log file
/// Include functions:
///     CheckLogFolder - Check if the folder Logs is present, remove old logs
///     StartLog - Start the log file stream
///     WriteLog - Write a line to the log file
///     EndLog - Close the log file stream
///
///--version 2.0.0.1
///Becket Hui 2024/7
///--version 1.0.1.1
///Becket Hui 2022/9
///
//////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.VisualStyles;
using System.Windows.Interop;
using System.Windows.Media;
using VMS.TPS.Common.Model.API;
using static System.Net.WebRequestMethods;

namespace batchOptimization
{
    internal class LogFile
    {
        private bool dirExist;
        private string fileDir;
        private string fileFullPath;
        private StreamWriter logWriter;
        private bool logExist;
        public RunTask CheckLogFolder(string dirPath)
        // Check if log file directory exists
        {
            fileDir = Path.Combine(dirPath, "Logs");
            if (Directory.Exists(fileDir))
            {
                dirExist = true;
                // Remove old logs
                string[] currLogFiles = Directory.GetFiles(fileDir, "*_log.txt");
                DateTime oneMonthAgo = DateTime.Now.AddMonths(-1);
                foreach (string logFile in currLogFiles)
                {
                    FileInfo logFileInfo = new FileInfo(logFile);
                    if (logFileInfo.CreationTime < oneMonthAgo)
                    {
                        logFileInfo.Delete();
                    }
                }
                return new RunTask(true, "Log will be saved.");
            }
            else
            {
                dirExist = false;
                return new RunTask(false, "Logs folder unavailable, no log will be saved.");
            }            
        }
        public void StartLog(string usrId)
        // Start the log file stream
        {
            if (dirExist)
            {
                string fileNm = DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + usrId + "_log.txt";
                fileFullPath = Path.Combine(fileDir, fileNm);
                try
                {
                    logWriter = new StreamWriter(fileFullPath);
                    logWriter.AutoFlush = true;
                    logExist = true;
                }
                catch (Exception ex)
                {
                    logExist = false;
                }
            }
            else
            {
                logExist = false;
            }
        }
        public void WriteLog(string txt)
        // Write a line to the log file
        {
            if (logExist)
            {
                logWriter.WriteLine(DateTime.Now.ToString("M/d/yy hh:mm:ss tt") + ": " + txt);
            }
        }
        public void EndLog()
        // Close the log file stream
        {
            if (logExist)
            {
                logWriter.Close();
                logExist = false;
            }
        }
    }
}
