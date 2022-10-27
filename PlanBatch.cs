//////////////////////////////////////////////////////////////////////
///Function to read the plan opt file
/// Include functions:
///     LoadFile - load the opt file and creates the ExtPlan objects for the listed plans
/// 
///--version 1.0.1.1
///Becket Hui 2022/9
///
//////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace batchOptimization
{
    internal class PlanBatch
    {
        public List<ExtPlan> PlanProperties { get; private set; } = new List<ExtPlan>(); // list of patient, course and plan id
        public Task LoadFile(string inputFile)
        // Load batch plan file
        {
            StreamReader readStream = new StreamReader(inputFile);
            // Skip title row
            readStream.ReadLine();
            while (!readStream.EndOfStream)
            {
                // Read input plan from csv file
                string[] inputVals = readStream.ReadLine().Split(';');
                try
                {
                    // Write plan properties to list
                    PlanProperties.Add(new ExtPlan(inputVals[0].Trim(), inputVals[1].Trim(), inputVals[2].Trim(), Convert.ToInt32(inputVals[3].Trim())));
                }
                catch
                {
                    PlanProperties.Clear();
                    readStream.Close();
                    return new Task(false, "Input file format is incorrect.");
                }
            }
            readStream.Close();
            return new Task(true, "Plans loaded.");
        }
    }
    internal class ExtPlan
    // Plan properties
    {
        public string PatientId;
        public string CourseId;
        public string PlanId;
        public int N_Runs;
        public ExtPlan(string ptId, string crsId, string plnId, int nRun)
        {
            PatientId = ptId;
            CourseId = crsId;
            PlanId = plnId;
            N_Runs = Math.Max(nRun, 1);
        }
    }
}
