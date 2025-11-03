//////////////////////////////////////////////////////////////////////
/// EsapiPlanOptimization.cs
/// BatchOptimization Library
///
/// Provides ESAPI-based routines for batch treatment plan optimization and dose calculation,
/// including multiple plan types and advanced beam/jaw management.
///
/// Main Capabilities:
///   - Connects to ARIA/ESAPI application (ConnectApp)
///   - Retrieves current user ID (GetUserId)
///   - Checks plan status for optimization readiness (CheckPlan)
///   - Performs plan optimization for IMRT, VMAT, and RapidArc Dynamic (Optimize)
///   - Computes dose, optionally using preset beam MU values (ComputeDose)
///   - Gracefully disconnects ESAPI session (Exit)
///   - Identifies plan/beam technique: IMRT, ARC, Mixed, RapidArc Dynamic, or None (GetBeamTechnique, IsPlanRapidArcDynamic)
///   - Automatically fits collimator jaws to Arc Optimization Aperture shape for RAD plans (FitAllCollimatorJawsToArcOptimizationAperture)
///   - Loads and applies optimizer configuration parameters from external files (LoadParameters)
///
///--version 3.0.0.1
///Becket Hui 2025/8
///--version 2.0.0.1
///Becket Hui 2024/7
///--version 1.0.1.1
///Becket Hui 2022/9
///
//////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace batchOptimization
{
    /// <summary>
    /// Performs ESAPI-driven tasks:
    /// - Connects to ARIA app
    /// - Checks, optimizes, calculates, and handles plan parameters
    /// </summary>
    internal class EsapiPlanOptimization
    {
        private VMS.TPS.Common.Model.API.Application EsapiApp;

        // Session state fields, only one active patient at a time
        private PatientSummary ptSumm;
        private Patient pt;
        private Course crs;
        private ExternalPlanSetup plnStup;

        // Default optimization parameters
        private int IMRT_maxIter = 5000;
        private OptimizationOption IMRT_initSt = OptimizationOption.RestartOptimization;
        private OptimizationOption VMAT_initSt = OptimizationOption.RestartOptimization;
        private OptimizationConvergenceOption IMRT_convergOpt = OptimizationConvergenceOption.TerminateIfConverged;
        private OptimizationIntermediateDoseOption IMRT_intDsOpt = OptimizationIntermediateDoseOption.UseIntermediateDose;
        private OptimizationIntermediateDoseOption VMAT_intDsOpt = OptimizationIntermediateDoseOption.UseIntermediateDose;

        /// <summary>
        /// Connects to the ARIA/ESAPI application.
        /// </summary>
        public RunTask ConnectApp()
        {
            try
            {
                EsapiApp = VMS.TPS.Common.Model.API.Application.CreateApplication();
                return new RunTask(true, string.Format("Aria connection successful."));
            }
            catch (Exception ex)
            {
                return new RunTask(false, string.Format("Aria connection failed:\n{0}", ex.Message));
            }
        }

        /// <summary>
        /// Gets the current user id (short name, no domain prefix).
        /// </summary>
        public string GetUserId()
        {
            string usrId = EsapiApp.CurrentUser.Id;
            int pos = usrId.LastIndexOf("\\") + 1;
            return usrId.Substring(pos, usrId.Length - pos);
        }

        /// <summary>
        /// Checks if a plan is ready for optimization.
        /// </summary>
        public RunTask CheckPlan(ExtPlan pln)
        {
            // Always close the previous patient before opening a new one
            try { EsapiApp.ClosePatient(); } catch { }
            ptSumm = EsapiApp.PatientSummaries.FirstOrDefault(ps => ps.Id == pln.PatientId);
            if (ptSumm == null) return new RunTask(false, string.Format("Patient {0} cannot be found.", pln.PatientId));

            try
            {
                pt = EsapiApp.OpenPatient(ptSumm);
                pt.BeginModifications();
            }
            catch
            {
                EsapiApp.ClosePatient();
                return new RunTask(false, string.Format("Cannot modify patient {0}. Make sure patient is closed. ", pln.PatientId));
            }

            crs = pt.Courses.FirstOrDefault(c => c.Id == pln.CourseId);
            if (crs == null)
            {
                EsapiApp.ClosePatient();
                return new RunTask(false, string.Format("Course {0} not found.", pln.CourseId));
            }

            plnStup = crs.ExternalPlanSetups.FirstOrDefault(p => p.Id == pln.PlanId);
            if (plnStup == null)
            {
                EsapiApp.ClosePatient();
                return new RunTask(false, string.Format("Plan {0} not found.", pln.PlanId));
            }

            if (plnStup.ApprovalStatus != PlanSetupApprovalStatus.UnApproved)
            {
                EsapiApp.ClosePatient();
                return new RunTask(false, string.Format("Plan {0} is not in unapproved status.", pln.PlanId));
            }

            try
            {
                var obj = (plnStup.OptimizationSetup != null)
                    ? plnStup.OptimizationSetup.Objectives.FirstOrDefault()
                    : null;
                EsapiApp.ClosePatient();
                return (obj != null)
                    ? new RunTask(true, string.Format("Plan {0} is ready for optimization.", pln.PlanId))
                    : new RunTask(false, string.Format("No objective in plan {0}.", pln.PlanId));
            }
            catch
            {
                EsapiApp.ClosePatient();
                return new RunTask(false, string.Format("Something is wrong with plan {0}.", pln.PlanId));
            }
        }

        /// <summary>
        /// Performs plan optimization for the specified plan and iteration number.
        /// Handles both IMRT and VMAT, including RapidArc Dynamic plans.
        /// </summary>
        public RunTask Optimize(ExtPlan pln, int idxOptRun)
        {
            try { EsapiApp.ClosePatient(); } catch { }

            try
            {
                // Find, open, and modify patient and course
                ptSumm = EsapiApp.PatientSummaries.FirstOrDefault(ps => ps.Id == pln.PatientId);
                if (ptSumm == null)
                    return new RunTask(false, string.Format("Patient {0} not found.", pln.PatientId));
                pt = EsapiApp.OpenPatient(ptSumm);
                pt.BeginModifications();

                crs = pt.Courses.FirstOrDefault(c => c.Id == pln.CourseId);
                if (crs == null)
                {
                    EsapiApp.ClosePatient();
                    return new RunTask(false, string.Format("Course {0} not found.", pln.CourseId));
                }

                plnStup = crs.ExternalPlanSetups.FirstOrDefault(p => p.Id == pln.PlanId);
                if (plnStup == null)
                {
                    EsapiApp.ClosePatient();
                    return new RunTask(false, string.Format("Plan {0} not found.", pln.PlanId));
                }

                string plnTech = GetBeamTechnique(plnStup);

                // --- IMRT Optimization ---
                if (plnTech == "IMRT")
                {
                    // Set optimization parameters based on run number and dose status
                    OptimizationOptionsIMRT options = new OptimizationOptionsIMRT(IMRT_maxIter, IMRT_initSt, IMRT_convergOpt, IMRT_intDsOpt, String.Empty);
                    if (idxOptRun == 1)
                    {
                        if (plnStup.Dose == null)
                        {
                            options = new OptimizationOptionsIMRT(IMRT_maxIter, OptimizationOption.RestartOptimization, IMRT_convergOpt, IMRT_intDsOpt, String.Empty);
                        }
                    }
                    else // subsequent run
                    {
                        options = new OptimizationOptionsIMRT(IMRT_maxIter, OptimizationOption.ContinueOptimization, IMRT_convergOpt, IMRT_intDsOpt, String.Empty);
                    }
                    // Start IMRT optimization
                    OptimizerResult optRes = plnStup.Optimize(options);
                    if (optRes.Success)
                    {
                        CalculationResult calcRes = plnStup.CalculateLeafMotions();  // Compute leaf sequence
                        EsapiApp.SaveModifications();
                        EsapiApp.ClosePatient();
                        return new RunTask(true, string.Format("Plan \"{0}\" completed optimization run no. {1}.", pln.PlanId, idxOptRun));
                    }
                    else
                    {
                        EsapiApp.ClosePatient();
                        return new RunTask(false, string.Format("Fail to optimize plan \"{0}\" during run no. {1}.", pln.PlanId, idxOptRun));
                    }
                }
                // --- VMAT Optimization, including RAD ---
                else if (plnTech == "VMAT")
                {
                    bool isRADPln = IsPlanRapidArcDynamic(plnStup);  // Detect RapidArc Dynamic

                    // Base optimization options for VMAT
                    OptimizationOptionsVMAT options = new OptimizationOptionsVMAT(VMAT_initSt, String.Empty);

                    if (idxOptRun == 1)
                    {
                        if (plnStup.Dose == null)
                        {
                            // If no dose presense, fit collimator jaws to arc optimization jaw
                            FitAllCollimatorJawsToArcOptimizationAperture(plnStup);
                            options = new OptimizationOptionsVMAT(VMAT_intDsOpt, String.Empty);
                        }
                        if (isRADPln)
                        {
                            // For RAD plans, fit collimator jaws to arc optimization jaw; restart optimization from fresh
                            FitAllCollimatorJawsToArcOptimizationAperture(plnStup);
                            options = new OptimizationOptionsVMAT(VMAT_intDsOpt, String.Empty);
                        }
                    }
                    else // subsequent run
                    {
                        options = new OptimizationOptionsVMAT(OptimizationOption.ContinueOptimization, String.Empty);
                        if (isRADPln)
                        {
                            EsapiApp.ClosePatient();
                            return new RunTask(false,
                                string.Format("Plan \"{0}\" is a RAD plan, skipping run no. {1} (>1 iteration not permitted).", pln.PlanId, idxOptRun));
                        }
                    }

                    // Start VMAT optimization
                    OptimizerResult optRes = plnStup.OptimizeVMAT(options);
                    if (optRes.Success)
                    {
                        EsapiApp.SaveModifications();
                        EsapiApp.ClosePatient();
                        return new RunTask(true, string.Format("Plan \"{0}\" completed optimization run no. {1}.", pln.PlanId, idxOptRun));
                    }
                    else
                    {
                        EsapiApp.ClosePatient();
                        return new RunTask(false, string.Format("Fail to optimize plan \"{0}\" during run no. {1}.", pln.PlanId, idxOptRun));
                    }
                }
                // --- Not pure IMRT or ARC plan ---
                else
                {
                    EsapiApp.ClosePatient();
                    return new RunTask(false,
                        string.Format("Cannot optimize \"{0}\": plan type is neither pure IMRT nor ARC (may be mixed or undefined).", pln.PlanId));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                if (pt != null) EsapiApp.ClosePatient();
                return new RunTask(false, string.Format("Fail to optimize plan \"{0}\".\n{1}", pln.PlanId, ex.ToString()));
            }
        }

        /// <summary>
        /// Computes dose for a plan. Uses preset MU if all MUs are valid, otherwise default calculation.
        /// </summary>
        public RunTask ComputeDose(ExtPlan pln)
        {
            try { EsapiApp.ClosePatient(); } catch { }

            try
            {
                ptSumm = EsapiApp.PatientSummaries.FirstOrDefault(ps => ps.Id == pln.PatientId);
                if (ptSumm == null)
                    return new RunTask(false, string.Format("Patient {0} not found.", pln.PatientId));

                pt = EsapiApp.OpenPatient(ptSumm);
                pt.BeginModifications();

                crs = pt.Courses.FirstOrDefault(c => c.Id == pln.CourseId);
                if (crs == null)
                {
                    EsapiApp.ClosePatient();
                    return new RunTask(false, string.Format("Course {0} not found.", pln.CourseId));
                }

                plnStup = crs.ExternalPlanSetups.FirstOrDefault(p => p.Id == pln.PlanId);
                if (plnStup == null)
                {
                    EsapiApp.ClosePatient();
                    return new RunTask(false, string.Format("Plan {0} not found.", pln.PlanId));
                }

                bool allMUValid = true;
                List<KeyValuePair<string, MetersetValue>> lsBmMU = new List<KeyValuePair<string, MetersetValue>>();
                foreach (Beam bm in plnStup.Beams)
                {
                    MetersetValue mu = bm.Meterset;
                    lsBmMU.Add(new KeyValuePair<string, MetersetValue>(bm.Id, mu));
                    if (double.IsNaN(mu.Value))
                        allMUValid = false;
                }

                CalculationResult calcRes;
                if (allMUValid)
                {
                    calcRes = plnStup.CalculateDoseWithPresetValues(lsBmMU);
                }
                else
                {
                    calcRes = plnStup.CalculateDose();
                }

                if (calcRes.Success)
                {
                    EsapiApp.SaveModifications();
                    EsapiApp.ClosePatient();
                    return new RunTask(true, string.Format("Plan \"{0}\" dose computation completed.", pln.PlanId));
                }
                else
                {
                    EsapiApp.ClosePatient();
                    return new RunTask(false, string.Format("Failed to compute dose for plan \"{0}\".", pln.PlanId));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                try { EsapiApp.ClosePatient(); } catch { }
                return new RunTask(false, string.Format("Failed to compute dose for plan \"{0}\":\n{1}", pln.PlanId, ex.Message));
            }
        }

        /// <summary>
        /// Disposes the ESAPI Application instance.
        /// </summary>
        public void Exit()
        {
            if (EsapiApp != null)
                EsapiApp.Dispose();
        }

        /// <summary>
        /// Identifies beam technique for provided plan: IMRT, ARC, Mixed, or null (none).
        /// </summary>
        private string GetBeamTechnique(ExternalPlanSetup plnStup)
        {
            bool hasStaticBeam = false;
            bool hasArcBeam = false;

            foreach (Beam bm in plnStup.Beams)
            {
                if (bm.IsSetupField)
                    continue;  // skip setup beams

                string techId = bm.Technique.Id.ToUpperInvariant();

                if (techId.Contains("STATIC"))
                    hasStaticBeam = true;

                if (techId.Contains("ARC"))
                    hasArcBeam = true;
            }

            if (hasStaticBeam && hasArcBeam)
            {
                return "Mixed";
            }
            else if (hasStaticBeam && !hasArcBeam)
            {
                return "IMRT";
            }
            else if (!hasStaticBeam && hasArcBeam)
            {
                return "VMAT";
            }
            else
            {
                return null;  // No treatment beams found
            }
        }

        /// <summary>
        /// Identify if the given plan is a RapidArc Dynamic plan.
        /// </summary>
        private bool IsPlanRapidArcDynamic(ExternalPlanSetup plnStup)
        {
            foreach (Beam bm in plnStup.Beams)
            {
                // Skip setup fields, only consider treatment beams
                if (bm.IsSetupField)
                    continue;

                string techId = bm.Technique.Id.ToUpperInvariant();

                // If beam uses static technique, skip this beam and continue checking others
                if (techId.Contains("STATIC"))
                    continue;

                // If beam uses arc technique, analyze control points for dynamic behavior
                if (techId.Contains("ARC"))
                {
                    BeamParameters bmPara = bm.GetEditableParameters();
                    if (bmPara != null)
                    {
                        double gantryAngPrev = double.NaN;
                        double gantryAngCurr = double.NaN;

                        foreach (ControlPointParameters cpPara in bmPara.ControlPoints)
                        {
                            if (Double.IsNaN(gantryAngPrev))
                            {
                                // Initialize first gantry angle
                                gantryAngPrev = cpPara.GantryAngle;
                            }
                            else
                            {
                                // Slide window of two consecutive gantry angles
                                gantryAngCurr = cpPara.GantryAngle;

                                // Check if two consecutive angles are equal (within 0.001 deg precision)
                                if (Math.Abs(gantryAngCurr - gantryAngPrev) < 1e-3)
                                {
                                    // Found repeated gantry angles indicating RapidArc Dynamic
                                    return true;
                                }

                                // Move forward for next iteration
                                gantryAngPrev = gantryAngCurr;
                            }
                        }
                    }
                }
            }

            // No RapidArc Dynamic pattern found across all beams
            return false;
        }

        /// <summary>
        /// Fit the collimator jaws to the Arc Optimization Aperture jaws in each beam in the plan.
        /// </summary>
        private void FitAllCollimatorJawsToArcOptimizationAperture(ExternalPlanSetup plnStup)
        {
            foreach (Beam bm in plnStup.Beams)
            {
                // Skip setup fields, only process treatment beams
                if (bm.IsSetupField)
                    continue;

                // Get arc optimization aperture jaw structure (may be null for some beams)
                ArcOptimizationAperture arcOpti_jaws = bm.ArcOptimizationAperture;

                // Construct the collimator jaw rectangle from aperture values
                VRect<double> coll_jaws = new VRect<double>(arcOpti_jaws.X1, arcOpti_jaws.X2, arcOpti_jaws.Y1, arcOpti_jaws.Y2);

                // Get editable beam parameters
                BeamParameters beamParams = bm.GetEditableParameters();
                if (beamParams == null)
                    continue;  // Skip if parameters not editable

                // Update every control point's jaw positions to match the aperture
                foreach (ControlPointParameters cp in beamParams.ControlPoints)
                {
                    cp.JawPositions = coll_jaws;
                }
            }
        }

        /// <summary>
        /// Loads config parameters for optimization from a text file.
        /// </summary>
        public RunTask LoadParameters(string filePath)
        {
            try
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    while (!sr.EndOfStream)
                    {
                        string line = sr.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string[] vals = line.Split(':');
                        if (vals.Length < 2) continue;
                        string key = vals[0].Trim();
                        string val = vals[1].Trim();

                        switch (key)
                        {
                            case "IMRT_maxiterations":
                                IMRT_maxIter = Convert.ToInt32(val);
                                break;
                            case "IMRT_initialState":
                                IMRT_initSt = (OptimizationOption)Convert.ToInt32(val);
                                break;
                            case "IMRT_convergenceOption":
                                IMRT_convergOpt = (OptimizationConvergenceOption)Convert.ToInt32(val);
                                break;
                            case "IMRT_intermediateDoseOption":
                                IMRT_intDsOpt = (OptimizationIntermediateDoseOption)Convert.ToInt32(val);
                                break;
                            case "VMAT_initialState":
                                VMAT_initSt = (OptimizationOption)Convert.ToInt32(val);
                                break;
                            case "VMAT_intermediateDoseOption":
                                VMAT_intDsOpt = (OptimizationIntermediateDoseOption)Convert.ToInt32(val);
                                break;
                            default:
                                // Ignore anything else
                                break;
                        }
                    }
                }
                return new RunTask(true, string.Format("Config file loaded."));
            }
            catch (Exception ex)
            {
                return new RunTask(false, string.Format("Error loading config file:\n{0}\nSome defaults may be used.", ex.Message));
            }
        }
    }
}
