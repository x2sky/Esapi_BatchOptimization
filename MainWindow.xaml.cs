//////////////////////////////////////////////////////////////////////
///Main window widget for batchOptimization script//
/// Include functions:
///     btnLoadPln_Click - routine to load plans from a .opt file
///     btnChckPln_Click - routine to check loaded plans for their status
///     btnRunOpt_Click - routine to run plan optimization and dose calculation
///     btnClrHist_Click - clear history view
///     CloseWindow - close the main window
///     ShowMessage - show message on history history and status bar
///     UpdatePlanLsView - show pending plans in the list view
/// 
///--version 1.0.1.1
///Becket Hui 2022/9
///
//////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;

[assembly: AssemblyVersion("1.0.1.1")]
[assembly: AssemblyFileVersion("0.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0")]

[assembly: ESAPIScript(IsWriteable = true)]

namespace batchOptimization
{
    /// <summary>
    /// This is the main window that contains the functions of the widgets
    /// </summary>
    public partial class MainWindow : Window
    {
        private EsapiPlanOptimization plnOpt = new EsapiPlanOptimization();
        private PlanBatch plnLs;
        private LogFile logger = new LogFile();
        private Task tsk;
        private Task tsk2;
        private CloseWindow closeWarning = new CloseWindow();
        private bool connection = false;
        private bool planLoad = false;
        private bool dsCalcOnly = false;
        public MainWindow()
        // initialize window and connect to Aria
        {
            InitializeComponent();
            // Connect to Aria
            tsk = plnOpt.ConnectApp();
            // Update windows
            if (tsk.success)
            {
                ShowMessage(tsk.message);
                string fileDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                // check Logs folder
                tsk2 = logger.CheckLogFolder(fileDir);
                if (tsk2.success == false) ShowMessage(tsk2.message);
                // load config file
                tsk2 = plnOpt.LoadParameters(Path.Combine(fileDir, "BatchOptimization.cfg"));
                if (tsk2.success == false) ShowMessage(tsk2.message);
                txtbStat.Text = "Ready to load batch file.";
                connection = true;
            }
            else
            {
                ShowMessage(tsk.message, Colors.Red);
                connection = false;
            }
        }
        private void btnLoadPln_Click(object sender, RoutedEventArgs e)
        // Read file that contains plans for optimization 
        {
            //Open file dialog box to select file//
            var infile = new OpenFileDialog
            {
                Multiselect = false,
                Title = "Choose batch optimization plan file",
                Filter = "Text Documents|*.txt"
            };
            if (infile.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                lstvPlns.Items.Clear();
                // Read the plan names from file
                plnLs = new PlanBatch();
                tsk = plnLs.LoadFile(infile.FileName);
                // Update windows
                if (tsk.success)
                {
                    UpdatePlanLsView();
                    ShowMessage(tsk.message);
                    planLoad = true;
                }
                else
                {
                    ShowMessage(tsk.message, Colors.Red);
                    planLoad= false;
                }
            }
            else
            {
                txtbStat.Text = "No file selected.";
            }
        }
        private void btnChckPln_Click(object sender, RoutedEventArgs e)
        // Check validity of plans //
        {
            if (connection && planLoad)
            {
                foreach (ExtPlan pln in plnLs.PlanProperties)
                {
                    tsk = plnOpt.CheckPlan(pln);
                    if (tsk.success)
                    {
                        ShowMessage(tsk.message);
                    }
                    else
                    {
                        ShowMessage(tsk.message, Colors.Red);
                    }
                }
                txtbStat.Text = "Ready.";
            }
            else if (!connection)
            {
                txtbStat.Text = "Cannot connect to database.";
            }
            else
            {
                txtbStat.Text = "No plan loaded.";
            }
        }
        private void btnRunOpt_Click(object sender, RoutedEventArgs e)
        // Start batch optimizations on plans
        {
            dsCalcOnly = chkBxDoseCalcOnly.IsChecked ?? false;
            if (connection && planLoad)
            {
                // set thread to close warning window by Esapi //
                CancellationTokenSource cts = new CancellationTokenSource();
                closeWarning.CloseWarningThread(cts.Token);
                // create log file stream //
                logger.StartLog(plnOpt.GetUserId());
                logger.WriteLog("Start batch processing.");
                foreach (ExtPlan pln in plnLs.PlanProperties)
                {
                    if (dsCalcOnly)  // if dose calc only
                    {
                        ShowMessage("Computing dose in plan \"" + pln.PlanId + "\".");
                        logger.WriteLog("For patient " + pln.PatientId + ", course " + pln.CourseId + ", plan " + pln.PlanId + ", start dose computation.");
                        tsk = plnOpt.ComputeDose(pln);
                        if (tsk.success)
                        {
                            ShowMessage(tsk.message);
                            logger.WriteLog(tsk.message);
                        }
                        else
                        {
                            ShowMessage(tsk.message, Colors.Red);
                            logger.WriteLog(tsk.message);
                        }
                    }
                    else  // if optimization
                    {
                        // First perform optimization
                        ShowMessage("Optimizing plan \"" + pln.PlanId + "\".");
                        logger.WriteLog("For patient " + pln.PatientId + ", course " + pln.CourseId + ", plan " + pln.PlanId + ", start optimization.");
                        for (int idxRun = 1; idxRun < pln.N_Runs+1; idxRun++)
                        {
                            logger.WriteLog("Start optimization run no." + idxRun.ToString() + ".");
                            tsk = plnOpt.Optimize(pln, idxRun);
                            if (tsk.success)
                            {
                                ShowMessage(tsk.message);
                                logger.WriteLog(tsk.message);
                            }
                            else
                            {
                                ShowMessage(tsk.message, Colors.Red);
                                logger.WriteLog(tsk.message);
                                break;  // exit then number of runs for loop
                            }
                        }
                        if (tsk.success)  // if pass optimization, perform dose calc
                        {
                            ShowMessage("Computing dose in plan \"" + pln.PlanId + "\".");
                            logger.WriteLog("Computing dose in plan \"" + pln.PlanId + "\".");
                            tsk = plnOpt.ComputeDose(pln);
                            if (tsk.success)
                            {
                                ShowMessage(tsk.message);
                                logger.WriteLog(tsk.message);
                            }
                            else
                            {
                                ShowMessage(tsk.message, Colors.Red);
                                logger.WriteLog(tsk.message);
                            }
                        }
                    }
                }
                // stop the close window thread
                cts.Cancel();
                // Close log file stream
                logger.WriteLog("All batch process completed.");
                logger.EndLog();
                // Clear the list of plans
                plnLs.PlanProperties.Clear();
                lstvPlns.Items.Clear();
                planLoad = false;
                ShowMessage("All batch process completed.");
                txtbStat.Text = "Ready to load batch file.";
            }
            else if (!connection)
            {
                txtbStat.Text = "Cannot connect to database.";
            }
            else
            {
                txtbStat.Text = "No plan loaded.";
            }
        }
        private void btnClrHist_Click(object sender, RoutedEventArgs e)
        // Clear history list view
        {
            lstvHst.Items.Clear();
        }
        private void CloseWindow(object sender, CancelEventArgs e)
        // Close window routine
        {
            plnOpt.Exit();
        }
        private void ShowMessage(String msg, Color? txtColor = null)
        // Write message to list view and status bar
        {
            // Add message to list view
            Dispatcher.BeginInvoke(new Action(delegate
            {
                System.Windows.Controls.ListViewItem itm = new System.Windows.Controls.ListViewItem();
                itm.Content = msg;
                itm.Foreground = new SolidColorBrush(txtColor ?? Colors.Black);
                lstvHst.Items.Add(itm);
                if (VisualTreeHelper.GetChildrenCount(lstvHst) > 0)
                {
                    Decorator bdrHst = VisualTreeHelper.GetChild(lstvHst, 0) as Decorator;
                    ScrollViewer svrHst = bdrHst.Child as ScrollViewer;
                    svrHst.ScrollToBottom();
                }
            }));
            // Make list view history update using a new thread
            lstvHst.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate { }));
            // Add message to status bar
            txtbStat.Text = msg;
            // Make status bar update using a new thread
            txtbStat.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate { }));
        }
        private void UpdatePlanLsView()
        // Add plan names to the plan list view
        {
            foreach (ExtPlan pln in plnLs.PlanProperties)
            {
                System.Windows.Controls.ListViewItem itm = new System.Windows.Controls.ListViewItem();
                itm.Content = pln.PatientId + ", " + pln.PlanId + ", " + pln.N_Runs.ToString() + " runs";
                lstvPlns.Items.Add(itm);
            }
        }
    }
    internal class Task
    {
        public bool success { get; set; }
        public string message { get; set; }
        public Task() { }
        public Task(bool scs, string msg)
        {
            success = scs;
            message = msg;
        }
    }
}
