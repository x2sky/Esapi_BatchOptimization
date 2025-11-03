//////////////////////////////////////////////////////////////////////
/// MainWindow.cs
/// BatchOptimization WPF Application
///
/// MainWindow class: UI window for managing batch plan optimization and dose calculation.
/// Provides functionality to add/remove/check plans, and run batch jobs with status logging.
/// 
/// Features:
/// - Connects to Aria via ESAPI (VMS Treatment Planning System API)
/// - Supports dose calculation only or iterative optimization runs per plan
/// - Maintains plan list with patient/course/plan identifiers and iteration counts
/// - Updates UI efficiently with INotifyPropertyChanged on plan input rows
/// - Runs long batch processes asynchronously with UI thread yielding for responsiveness
/// - Automatically closes ESAPI warning popups in background during optimization
/// - Logs progress and results to disk and UI log view
///
/// Author: Becket Hui
/// Date: August 2025
///
/// Version History:
/// - v3.0.0.1 (2025/08) Upgrade to ESAPI version 18.1, improved async batch processing and UI responsiveness, add calculate with MU option, modify optimization to handle RAD plan type
/// - v2.0.0.1 (2024/07) Refactor using INotifyPropertyChanged and Dispatcher.Yield for smoother UI updates
/// - v1.0.1.1 (2022/09) Initial release with basic batch optimization UI and ESAPI integration
///
//////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;

[assembly: AssemblyVersion("3.0.0.7")]
[assembly: AssemblyFileVersion("0.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0")]

[assembly: ESAPIScript(IsWriteable = true)]

namespace batchOptimization
{
    /// <summary>
    /// Main window class for batch optimization UI.
    /// Handles batch plan optimization and dose calc executions.
    /// </summary>
    public partial class MainWindow : Window
    {
        // Instance of your optimization manager class wrapping ESAPI logic
        private EsapiPlanOptimization plnOpt = new EsapiPlanOptimization();

        // Logger to write logs to file
        private LogFile logger = new LogFile();

        // Helper class to close warning popups automatically in background
        private CloseWindow closeWarning = new CloseWindow();

        // Indicates connection status to Aria database
        private bool connection = false;

        // Flag indicating if only dose calculation (no optimization) is requested
        private bool dsCalcOnly = false;

        /// <summary>
        /// MainWindow constructor initializes components and connects to Aria.
        /// Loads configuration and prepares UI.
        /// </summary>
        public MainWindow()
        {
            RunTask tskApp, tskLogLoc;
            InitializeComponent();

            // Connect to Aria application via plnOpt helper
            tskApp = plnOpt.ConnectApp();

            if (tskApp.success)
            {
                ShowMessage(tskApp.message); // Show connection message

                // Setup file directories for logs and config
                string fileDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

                // Ensure Logs folder exist or create it
                tskLogLoc = logger.CheckLogFolder(fileDir);
                if (!tskLogLoc.success) ShowMessage(tskLogLoc.message);

                // Load batch optimization parameters from config file
                tskLogLoc = plnOpt.LoadParameters(Path.Combine(fileDir, "BatchOptimization.cfg"));
                if (!tskLogLoc.success) ShowMessage(tskLogLoc.message);

                // Initialize the DataGrid with a blank PlanInput
                dataGridPlns.ItemsSource = new ObservableCollection<PlanInput> { new PlanInput() };

                txtbStat.Text = "Ready.";
                connection = true;
            }
            else
            {
                // If connection failed, disable functionality and show error
                ShowMessage(tskApp.message, Colors.Red);
                txtbStat.Text = "Failed to connect to Aria.";
                connection = false;
            }
        }

        /// <summary>
        /// Adds a new plan row to the DataGrid.
        /// Copies the currently selected plan if any; otherwise adds a blank row.
        /// </summary>
        private void btnAddPln_Click(object sender, RoutedEventArgs e)
        {
            PlanInput selPln = dataGridPlns.SelectedItem as PlanInput;

            ObservableCollection<PlanInput> dataGridColl = dataGridPlns.ItemsSource as ObservableCollection<PlanInput>;

            // If null (no plans yet), initialize the collection and bind it
            if (dataGridColl == null)
            {
                dataGridColl = new ObservableCollection<PlanInput>();
                dataGridPlns.ItemsSource = dataGridColl;
            }

            if (selPln != null)
            {
                // Copy the selected plan's properties into a new plan and add
                PlanInput newPln = new PlanInput();
                newPln.Pat = selPln.Pat;
                newPln.Crs = selPln.Crs;
                newPln.Pln = selPln.Pln;
                newPln.Iter = selPln.Iter;
                newPln.Stat = "⨁";  // Default status symbol

                dataGridColl.Add(newPln);
            }
            else
            {
                // No selection: add a new blank plan
                dataGridColl.Add(new PlanInput());
            }
        }

        /// <summary>
        /// Removes the currently selected plan row from the DataGrid.
        /// </summary>
        private void btnRmvPln_Click(object sender, RoutedEventArgs e)
        {
            if (dataGridPlns.SelectedItem != null)
            {
                ObservableCollection<PlanInput> dataGridColl = dataGridPlns.ItemsSource as ObservableCollection<PlanInput>;

                if (dataGridColl != null)
                {
                    dataGridColl.Remove((PlanInput)dataGridPlns.SelectedItem);
                }
            }
        }

        /// <summary>
        /// Validates each plan in the DataGrid by invoking CheckPlan method.
        /// Updates each plan's status and logs accordingly.
        /// </summary>
        private async void btnChckPln_Click(object sender, RoutedEventArgs e)
        {
            RunTask tskChckPln;
            if (!connection)
            {
                txtbStat.Text = "Cannot connect to database.";
                return;
            }

            ObservableCollection<PlanInput> dataGridColl = dataGridPlns.ItemsSource as ObservableCollection<PlanInput>;
            if (dataGridColl == null || dataGridColl.Count == 0)
            {
                txtbStat.Text = "No plan added to list.";
                return;
            }

            int rowNumber = 0;
            ShowMessage(string.Format("Checking {0} plans.", dataGridColl.Count));
            txtbStat.Text = "Checking plans.";

            foreach (PlanInput inPln in dataGridColl)
            {
                rowNumber++;

                if (inPln == null)
                {
                    continue;
                }

                try
                {
                    // Convert PlanInput to Extended plan info for ESAPI calls
                    ExtPlan extPln = new ExtPlan(inPln);

                    tskChckPln = plnOpt.CheckPlan(extPln);

                    // Yield UI thread so window stays responsive to input (minimize, updates, etc.)
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    // Update status and message based on check result
                    if (tskChckPln.success)
                    {
                        ShowMessage(string.Format("For row {0}, {1}", rowNumber, tskChckPln.message));
                        inPln.Stat = "⨁";  // Indicate valid
                    }
                    else
                    {
                        ShowMessage(string.Format("For row {0}, {1}", rowNumber, tskChckPln.message), Colors.Red);
                        inPln.Stat = "✕";  // Indicate invalid
                    }
                }
                catch
                {
                    ShowMessage(string.Format("For row {0}, input is invalid.", rowNumber), Colors.Red);
                    inPln.Stat = "✕";
                }
            }

            txtbStat.Text = "Ready.";
        }

        /// <summary>
        /// Runs batch optimization or dose calculation asynchronously.
        /// Disables UI controls during processing, yields control frequently to keep UI responsive.
        /// Monitors a background popup-closer thread for faults.
        /// </summary>
        private async void btnRunOpt_Click(object sender, RoutedEventArgs e)
        {
            RunTask tskOpt, tskCalcDs;
            txtbStat.Text = "Running batch processing.";
            dsCalcOnly = chkBxDoseCalcOnly.IsChecked.HasValue ? chkBxDoseCalcOnly.IsChecked.Value : false;

            // Disable UI controls to prevent user changes mid-processing
            EnableControls(false);

            if (!connection)
            {
                txtbStat.Text = "Cannot connect to database.";
                EnableControls(true);
                return;
            }

            var cts = new CancellationTokenSource();

            try
            {
                // Start background task that closes warning popups during ESAPI calls
                Task closeWarningTask = closeWarning.CloseWarningThread(cts.Token);

                // ContinueWith monitors and reports exceptions on UI thread context
                _ = closeWarningTask.ContinueWith((t) =>
                {
                    if (t.IsFaulted)
                    {
                        Exception ex = t.Exception.InnerException ?? t.Exception;
                        ShowMessage(string.Format("Something wrong with routine to close pop-up windows:\n{0}", ex.Message), Colors.Red);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());

                // Start log file and write initial message
                logger.StartLog(plnOpt.GetUserId());
                logger.WriteLog(string.Format("Start batch processing."));

                ObservableCollection<PlanInput> dataGridColl = dataGridPlns.ItemsSource as ObservableCollection<PlanInput>;

                int rowNumber = 0;

                foreach (PlanInput inPln in dataGridColl)
                {
                    rowNumber++;

                    if (inPln == null)
                    {
                        continue;
                    }

                    try
                    {
                        ExtPlan extPln = new ExtPlan(inPln);

                        if (dsCalcOnly)
                        {
                            // If in Dose Calculation Only mode, just compute dose
                            ShowMessage(string.Format("For row {0}, plan {1}, computing dose.", rowNumber, extPln.PlanId));
                            logger.WriteLog(string.Format("For patient {0}, course {1}, plan {2}, start dose computation.", extPln.PatientId, extPln.CourseId, extPln.PlanId));
 
                            // Yield for responsiveness
                            await Dispatcher.Yield(DispatcherPriority.Background);
                            
                            tskCalcDs = plnOpt.ComputeDose(extPln);

                            // Update results and status accordingly
                            if (tskCalcDs.success)
                            {
                                ShowMessage(tskCalcDs.message);
                                logger.WriteLog(tskCalcDs.message);
                                inPln.Stat = "✓";
                            }
                            else
                            {
                                ShowMessage(tskCalcDs.message, Colors.Red);
                                logger.WriteLog(tskCalcDs.message);
                                inPln.Stat = "✕";
                            }
                        }
                        else
                        {
                            // Optimization mode: run multiple optimization iterations per plan
                            ShowMessage(string.Format("For row {0}, plan {1}, start optimization.", rowNumber, extPln.PlanId));
                            logger.WriteLog(string.Format("For patient {0}, course {1}, plan {2}, start optimization.", extPln.PatientId, extPln.CourseId, extPln.PlanId));

                            for (int idxRun = 1; idxRun <= extPln.N_Runs; idxRun++)
                            {
                                ShowMessage(string.Format("Running optimization iteration no. {0}.", idxRun));
                                logger.WriteLog(string.Format("Start optimization run no. {0} in plan \"{1}\".", idxRun, extPln.PlanId));

                                // Yield for responsiveness
                                await Dispatcher.Yield(DispatcherPriority.Background);

                                tskOpt = plnOpt.Optimize(extPln, idxRun);

                                if (tskOpt.success)
                                {
                                    logger.WriteLog(tskOpt.message);
                                    ShowMessage(string.Format("Optimization iteration no. {0} completed, computing dose.", idxRun));
                                    logger.WriteLog(string.Format("Start dose computation in plan \"{0}\".", extPln.PlanId));

                                    tskCalcDs = plnOpt.ComputeDose(extPln);

                                    if (tskCalcDs.success)
                                    {
                                        ShowMessage(tskCalcDs.message);
                                        logger.WriteLog(tskCalcDs.message);
                                        inPln.Stat = "✓";
                                    }
                                    else
                                    {
                                        ShowMessage(tskCalcDs.message, Colors.Red);
                                        logger.WriteLog(tskCalcDs.message);
                                        inPln.Stat = "✕";
                                        break;  // Stop further iterations on failure
                                    }
                                }
                                else
                                {
                                    ShowMessage(tskOpt.message, Colors.Red);
                                    logger.WriteLog(tskOpt.message);
                                    inPln.Stat = "✕";
                                    break; // Stop further iterations on failure
                                }

                                // Yield for responsiveness
                                await Dispatcher.Yield(DispatcherPriority.Background);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Show exception details and mark plan as failed
                        ShowMessage(string.Format("For row {0}, unexpected fault.", rowNumber), Colors.Red);
                        logger.WriteLog(string.Format("Exception in processing row {0} : {1}", rowNumber, ex.Message));
                        inPln.Stat = "✕";
                    }

                    // Yield between plans too for responsiveness
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }

                // Finish logging and update UI
                logger.WriteLog(string.Format("All batch process completed."));
                ShowMessage(string.Format("All batch process completed."));
                txtbStat.Text = "Ready.";
                logger.EndLog();
            }
            finally
            {
                // Cancel popup-closing thread and free token source
                cts.Cancel();
                cts.Dispose();

                // Enable controls back for user interaction
                EnableControls(true);
            }
        }

        /// <summary>
        /// Enables or disables the main UI controls for plan input and batch processing.
        /// </summary>
        /// <param name="enable">True to enable, false to disable controls.</param>
        private void EnableControls(bool enable)
        {
            dataGridPlns.IsReadOnly = !enable;      // If enabling, DataGrid is editable; else read-only
            btnAddPln.IsEnabled = enable;
            btnRmvPln.IsEnabled = enable;
            btnChckPln.IsEnabled = enable;
            btnRunOpt.IsEnabled = enable;
            chkBxDoseCalcOnly.IsEnabled = enable;
        }

        /// <summary>
        /// Cleanly exits the ESAPI app when main window closes.
        /// </summary>
        private void CloseWindow(object sender, CancelEventArgs e)
        {
            plnOpt.Exit();
        }

        /// <summary>
        /// Shows a message both in the ListView history and scrolls message box to bottom.
        /// Safely marshals to UI thread using Dispatcher.
        /// </summary>
        private void ShowMessage(string msg, Color? txtColor = null)
        {
            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                ListViewItem itm = new ListViewItem();
                itm.Content = msg;
                itm.Foreground = new SolidColorBrush(txtColor.HasValue ? txtColor.Value : Colors.Black);
                lstvHst.Items.Add(itm);

                // Scroll to the bottom so newest messages are visible
                if (VisualTreeHelper.GetChildrenCount(lstvHst) > 0)
                {
                    Decorator border = VisualTreeHelper.GetChild(lstvHst, 0) as Decorator;
                    ScrollViewer scrollViewer = border.Child as ScrollViewer;
                    scrollViewer.ScrollToBottom();
                }
            }));
        }
    }

    /// <summary>
    /// Extended plan data is used for ESAPI calls.
    /// </summary>
    internal class ExtPlan
    {
        public string PatientId;
        public string CourseId;
        public string PlanId;
        public int N_Runs;

        public ExtPlan(PlanInput pln)
        {
            PatientId = pln.Pat;
            CourseId = pln.Crs;
            PlanId = pln.Pln;
            N_Runs = Math.Max(pln.Iter, 1);
        }
    }

    /// <summary>
    /// Plan input row model with change notification for UI updates.
    /// </summary>
    internal class PlanInput : INotifyPropertyChanged
    {
        private string pat = "";
        public string Pat { get => pat; set { pat = value; OnPropertyChanged(nameof(Pat)); } }

        private string crs = "";
        public string Crs { get => crs; set { crs = value; OnPropertyChanged(nameof(Crs)); } }

        private string pln = "";
        public string Pln { get => pln; set { pln = value; OnPropertyChanged(nameof(Pln)); } }

        private int iter = 1;
        public int Iter { get => iter; set { iter = value; OnPropertyChanged(nameof(Iter)); } }

        private string stat = "⨁";
        public string Stat { get => stat; set { stat = value; OnPropertyChanged(nameof(Stat)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    /// <summary>
    /// Simple class to hold result and message for each batch task.
    /// </summary>
    internal class RunTask
    {
        public bool success { get; set; }
        public string message { get; set; }

        public RunTask()
        {
        }

        public RunTask(bool scs, string msg)
        {
            success = scs;
            message = msg;
        }
    }
}
