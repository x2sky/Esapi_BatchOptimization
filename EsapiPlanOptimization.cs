//////////////////////////////////////////////////////////////////////
///Function to perform the Esapi related tasks 
/// Include functions:
///     ConnectApp - connect to Aria app
///     GetUserId - get current user id
///     CheckPlan - check the status of the plan for optimization
///     Optimize - perform plan optimization
///     ComputeDose - compute dose
///     Exit - disconnect from Aria app
///     getBeamTechnique - get the beam technique (from the first non-setup beam)
///     LoadParameters - load parameters from config file
///
///--version 1.0.1.1
///Becket Hui 2022/9
///
//////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace batchOptimization
{
    internal class EsapiPlanOptimization
    {
        private VMS.TPS.Common.Model.API.Application EsapiApp;
        private PatientSummary ptSumm;
        private Patient pt;
        private Course crs;
        private ExternalPlanSetup plnStup;
        // default optimization options //
        private int IMRT_maxIter = 5000;
        private OptimizationOption IMRT_initSt = OptimizationOption.RestartOptimization;
        private OptimizationOption VMAT_initSt = OptimizationOption.RestartOptimization;
        private OptimizationConvergenceOption IMRT_convergOpt = OptimizationConvergenceOption.TerminateIfConverged;
        private OptimizationIntermediateDoseOption IMRT_intDsOpt = OptimizationIntermediateDoseOption.UseIntermediateDose;
        private OptimizationIntermediateDoseOption VMAT_intDsOpt = OptimizationIntermediateDoseOption.UseIntermediateDose;

        public Task ConnectApp()
        // Connect to Aria
        {
            try
            {
                EsapiApp = VMS.TPS.Common.Model.API.Application.CreateApplication();
                return new Task(true, "Aria connection successful.");
            }
            catch
            {
                return new Task(false, "Aria connection failed.");
            }
        }
        public string GetUserId()
        // Get user ID
        {
            string usrId = EsapiApp.CurrentUser.Id;
            int pos = usrId.LastIndexOf("\\") + 1;
            return usrId.Substring(pos, usrId.Length-pos);
        }
        public Task CheckPlan(ExtPlan pln)
        // Check the optimization status of the selected plan
        {
            ptSumm = EsapiApp.PatientSummaries.FirstOrDefault(ps => ps.Id == pln.PatientId);
            if (ptSumm == null) return new Task(false, "Patient " + pln.PatientId + " cannot be found.");
            try
            {
                pt = EsapiApp.OpenPatient(ptSumm);
                pt.BeginModifications();
            }
            catch (Exception ex)
            {
                EsapiApp.ClosePatient();
                return new Task(false, "Cannot modify patient " + pln.PatientId + ", make sure patient is closed.");
            }
            crs = pt.Courses.FirstOrDefault(c => c.Id == pln.CourseId);
            if (crs == null)
            {
                EsapiApp.ClosePatient();
                return new Task(false, "Course " + pln.CourseId + " cannot be found.");
            }
            plnStup = crs.ExternalPlanSetups.FirstOrDefault(p => p.Id == pln.PlanId);
            if (plnStup == null)
            {
                EsapiApp.ClosePatient();
                return new Task(false, "Plan " + pln.PlanId + " cannot be found.");
            }
            if (plnStup.ApprovalStatus != PlanSetupApprovalStatus.UnApproved)
            {
                EsapiApp.ClosePatient();
                return new Task(false, "Plan " + pln.PlanId + " is not in unapproved status.");
            }
            try
            {
                IEnumerable<OptimizationObjective> objLs = plnStup.OptimizationSetup.Objectives;
                if (objLs != null)
                {
                    OptimizationObjective obj1 = objLs.FirstOrDefault();
                    if (obj1 != null)
                    {
                        EsapiApp.ClosePatient();
                        return new Task(true, "Plan " + pln.PlanId + " is ready for optimization.");
                    }
                }
                EsapiApp.ClosePatient();
                return new Task(false, "No objective in plan " + pln.PlanId + ".");
            }
            catch (Exception ex)
            {
                // MessageBox.Show(ex.ToString());
                EsapiApp.ClosePatient();
                return new Task(false, "Something is wrong with plan " + pln.PlanId + ".");
            }
        }
        public Task Optimize(ExtPlan pln, int idxOptRun)
        // Plan optimization
        {
            try
            {
                ptSumm = EsapiApp.PatientSummaries.FirstOrDefault(ps => ps.Id == pln.PatientId);
                pt = EsapiApp.OpenPatient(ptSumm);
                pt.BeginModifications();
                crs = pt.Courses.FirstOrDefault(c => c.Id == pln.CourseId);
                plnStup = crs.ExternalPlanSetups.FirstOrDefault(p => p.Id == pln.PlanId);
                string plnTech = GetBeamTechnique(plnStup);  // find if plan is fixed beam or arc
                if (plnTech == "IMRT")
                {
                    // Set optimization parameters
                    OptimizationOptionsIMRT options = new OptimizationOptionsIMRT(IMRT_maxIter, IMRT_initSt, IMRT_convergOpt, IMRT_intDsOpt, String.Empty);
                    if (idxOptRun == 1)  // first run
                    {
                        if (plnStup.Dose == null)  // if there is no dose present
                        {
                            // Must restart optimization
                            options = new OptimizationOptionsIMRT(IMRT_maxIter, OptimizationOption.RestartOptimization, IMRT_convergOpt, IMRT_intDsOpt, String.Empty);
                        }
                    }
                    else  // subsequent run
                    {
                        // start subsequent run with continue optimization
                        options = new OptimizationOptionsIMRT(IMRT_maxIter, OptimizationOption.ContinueOptimization, IMRT_convergOpt, IMRT_intDsOpt, String.Empty);
                    }
                    // Start optimization
                    OptimizerResult optRes = plnStup.Optimize(options);
                    // MessageBox.Show("N optimizations = " + optRes.NumberOfIMRTOptimizerIterations.ToString() + ", and it's success is " + optRes.Success.ToString());
                    if (optRes.Success)
                    {
                        CalculationResult calcRes = plnStup.CalculateLeafMotions();  // compute leaf sequence
                        EsapiApp.SaveModifications();
                        EsapiApp.ClosePatient();
                        return new Task(true, "Plan \"" + pln.PlanId + "\" completed optimization run no." + idxOptRun.ToString() + ".");
                    }
                    else
                    {
                        EsapiApp.ClosePatient();
                        return new Task(false, "Fail to optimize plan \"" + pln.PlanId + "\" during run no." + idxOptRun.ToString() + ".");
                    }
                }
                else if (plnTech == "VMAT")
                {
                    // Set optimization parameters
                    OptimizationOptionsVMAT options = new OptimizationOptionsVMAT(VMAT_initSt, String.Empty);
                    if (idxOptRun == 1)  // first run
                    {
                        if (plnStup.Dose == null)  // if there is no dose present
                        {
                            options = new OptimizationOptionsVMAT(VMAT_intDsOpt, String.Empty);  // must restart optimization, use intermediate dose option
                        }
                    }
                    else  // subsequent run
                    {
                        options = new OptimizationOptionsVMAT(OptimizationOption.ContinueOptimization, String.Empty);  // start subsequent run with continue optimization
                    }
                    // Start optimization
                    OptimizerResult optRes = plnStup.OptimizeVMAT(options);
                    // MessageBox.Show("N optimizations = " + optRes.NumberOfIMRTOptimizerIterations.ToString() + ", and it's success is " + optRes.Success.ToString());
                    if (optRes.Success)
                    {
                        EsapiApp.SaveModifications();
                        EsapiApp.ClosePatient();
                        return new Task(true, "Plan \"" + pln.PlanId + "\" completed optimization run no." + idxOptRun.ToString() + ".");
                    }
                    else
                    {
                        EsapiApp.ClosePatient();
                        return new Task(false, "Fail to optimize plan \"" + pln.PlanId + "\" during run no." + idxOptRun.ToString() + ".");
                    }
                }
                else  // null
                {
                    EsapiApp.ClosePatient();
                    return new Task(false, "Cannot optimize \"" + pln.PlanId + "\", it is neither IMRT nor VMAT.");
                }
            }
            catch (Exception ex)
            {
                // MessageBox.Show(ex.ToString());
                if (pt != null) EsapiApp.ClosePatient();
                return new Task(false, "Fail to optimize plan \"" + pln.PlanId + "\".");
            }

        }
        public Task ComputeDose(ExtPlan pln)
        // Dose Calculation
        {
            try
            {
                ptSumm = EsapiApp.PatientSummaries.FirstOrDefault(ps => ps.Id == pln.PatientId);
                pt = EsapiApp.OpenPatient(ptSumm);
                pt.BeginModifications();
                crs = pt.Courses.FirstOrDefault(c => c.Id == pln.CourseId);
                plnStup = crs.ExternalPlanSetups.FirstOrDefault(p => p.Id == pln.PlanId);
                CalculationResult calcRes = plnStup.CalculateDose();
                // MessageBox.Show("Dose calc success is " + calcRes.Success.ToString());
                EsapiApp.SaveModifications();
                EsapiApp.ClosePatient();
                return new Task(true, "Plan \"" + pln.PlanId + "\" dose computation completed.");
            }
            catch (Exception ex)
            {
                // MessageBox.Show(ex.ToString());
                if (pt != null) EsapiApp.ClosePatient();
                return new Task(false, "Fail to compute dose in plan \"" + pln.PlanId + "\".");
            }
        }
        public void Exit()
        // Release resources
        {
            if (EsapiApp != null) EsapiApp.Dispose();
        }
        private string GetBeamTechnique(ExternalPlanSetup plnStup)
        // Get beam technique to see if it is fixed beam or arc
        {
            Beam bm = plnStup.Beams.FirstOrDefault(b => b.IsSetupField == false);
            if (bm.Technique.Id.ToString().Contains("STATIC"))
            {
                return "IMRT";
            }
            else if (bm.Technique.Id.ToString().Contains("ARC"))
            {
                return "VMAT";
            }
            else
            {
                return null;
            }
        }
        public Task LoadParameters(string filePath)
        // Read config file to obtain parameters
        {
            try
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    while (!sr.EndOfStream)
                    {
                        // Read parameters from cfg file
                        string[] vals = sr.ReadLine().Split(':');
                        // assign parameters
                        switch (vals[0].Trim())
                        {
                            case "IMRT_maxiterations":
                                IMRT_maxIter = Convert.ToInt32(vals[1].Trim());
                                break;
                            case "IMRT_initialState":
                                IMRT_initSt = (OptimizationOption)Convert.ToInt32(vals[1].Trim());
                                break;
                            case "IMRT_convergenceOption":
                                IMRT_convergOpt = (OptimizationConvergenceOption)Convert.ToInt32(vals[1].Trim());
                                break;
                            case "IMRT_intermediateDoseOption":
                                IMRT_intDsOpt = (OptimizationIntermediateDoseOption)Convert.ToInt32(vals[1].Trim());
                                break;
                            case "VMAT_initialState":
                                VMAT_initSt = (OptimizationOption)Convert.ToInt32(vals[1].Trim());
                                break;
                            case "VMAT_intermediateDoseOption":
                                VMAT_intDsOpt = (OptimizationIntermediateDoseOption)Convert.ToInt32(vals[1].Trim());
                                break;
                            default:
                                // do nothing if extra line //
                                break;
                        }
                    }
                }
                return new Task(true, "Config file loaded.");
            }
            catch (Exception ex)
            {
                //MessageBox.Show(ex.Message);
                return new Task(false, "Error loading config file, some default parameter values may be used.");
            }
        }
    }
}
