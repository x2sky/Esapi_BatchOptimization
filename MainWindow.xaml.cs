//////////////////////////////////////////////////////////////////////
///Main window widget for batchOptimization script//
/// Include functions:
///     btnAddPln_Click - routine to add a plan row to the plan list
///     btnRmvPln_Click - routine to remove a plan row
///     btnChckPln_Click - routine to check loaded plans for their status
///     btnRunOpt_Click - routine to run plan optimization and dose calculation
///     CloseWindow - close the main window
///     ShowMessage - show message on history history and status bar
///
///--version 2.0.0.1
///Becket Hui 2024/7
///--version 1.0.1.1
///Becket Hui 2022/9
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
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;

[assembly: AssemblyVersion("2.0.0.1")]
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
        private LogFile logger = new LogFile();
        private RunTask tsk;
        private RunTask tsk2;
        private CloseWindow closeWarning = new CloseWindow();
        private bool connection = false;
        private bool dsCalcOnly = false;
        public MainWindow()
        // Initialize window and connect to Aria
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
                dataGridPlns.ItemsSource = new ObservableCollection<PlanInput>
                {
                    new PlanInput()
                };
                txtbStat.Text = "Ready.";
                connection = true;
            }
            else
            {
                ShowMessage(tsk.message, Colors.Red);
                txtbStat.Text = "Failed to connect to Aria.";
                connection = false;
            }
        }
        private void btnAddPln_Click(object sender, RoutedEventArgs e)
        // Add a new plan row to dataGridPlns
        {
            PlanInput selPln = (PlanInput)dataGridPlns.SelectedItem;
            ObservableCollection<PlanInput> dataGridColl;
            if (dataGridPlns.ItemsSource == null)
            {
                // If dataGridPlns is not bind to any collection, add a new collection
                dataGridColl = new ObservableCollection<PlanInput>
                {
                    new PlanInput()
                };
            }
            else
            {
                dataGridColl = (ObservableCollection<PlanInput>)dataGridPlns.ItemsSource;

                if (selPln != null)
                {
                    // If row selected, copy the selected row
                    PlanInput newPln = new PlanInput
                    {
                        Pat = selPln.Pat,
                        Crs = selPln.Crs,
                        Pln = selPln.Pln,
                        Iter = selPln.Iter,
                        Stat = "⨁"
                    };
                    // Add the copied plan to collection
                    dataGridColl.Add(newPln);
                }
                else
                {
                    // If no row selected, add a new blank row to collection
                    dataGridColl.Add(new PlanInput());
                }
            }
            dataGridPlns.ItemsSource = dataGridColl;
        }
        private void btnRmvPln_Click(object sender, RoutedEventArgs e)
        // Remove the selected plan row
        {
            if (dataGridPlns.SelectedItem != null)
            {
                ObservableCollection<PlanInput> dataGridColl = (ObservableCollection<PlanInput>)dataGridPlns.ItemsSource;
                dataGridColl.Remove((PlanInput)dataGridPlns.SelectedItem);
            }
        }
        private void btnChckPln_Click(object sender, RoutedEventArgs e)
        // Check validity of plans //
        {
            if (connection)
            {
                int plnCnt = dataGridPlns.Items.Count;
                int rowNumber = 0;
                if (plnCnt == 0)
                {
                    txtbStat.Text = "No plan added to list.";
                }
                else
                {
                    if (dataGridPlns.ItemsSource == null)
                    {
                        txtbStat.Text = "No plan added to list.";
                    }
                    else
                    {
                        ObservableCollection<PlanInput> dataGridColl = (ObservableCollection<PlanInput>)dataGridPlns.ItemsSource;
                        ShowMessage("Checking " + plnCnt + " plans.");
                        txtbStat.Text = "Checking plans.";
                        foreach (PlanInput inPln in dataGridColl)
                        {
                            rowNumber++;
                            if (inPln != null)
                            {
                                try
                                {
                                    ExtPlan extPln = new ExtPlan(inPln);
                                    tsk = plnOpt.CheckPlan(extPln);
                                    if (tsk.success)
                                    {
                                        ShowMessage("For row " + rowNumber + ", " + tsk.message);
                                        inPln.Stat = "⨁";
                                    }
                                    else
                                    {
                                        ShowMessage("For row " + rowNumber + ", " + tsk.message, Colors.Red);
                                        inPln.Stat = "✕";
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ShowMessage("For row " + rowNumber + ", input is invalid.", Colors.Red);
                                    inPln.Stat = "✕";
                                }
                            }
                        }
                        dataGridPlns.Items.Refresh();
                        txtbStat.Text = "Ready.";
                    }
                }
            }
            else
            {
                txtbStat.Text = "Cannot connect to database.";
            }
        }
        private void btnRunOpt_Click(object sender, RoutedEventArgs e)
        // Start batch optimizations on plans
        {
            txtbStat.Text = "Running batch processing.";
            dsCalcOnly = chkBxDoseCalcOnly.IsChecked ?? false;
            // Disable all buttons
            Dispatcher.Invoke(() =>
            {
                dataGridPlns.IsReadOnly = true;
                btnAddPln.IsEnabled = false;
                btnRmvPln.IsEnabled = false;
                btnChckPln.IsEnabled = false;
                btnRunOpt.IsEnabled = false;
                chkBxDoseCalcOnly.IsEnabled = false;
            });
            if (connection)
            {
                // Set thread to close warning window by Esapi
                CancellationTokenSource cts = new CancellationTokenSource();
                //closeWarning.CloseWarningThread(cts.Token);
                Task closeWarningTask = closeWarning.CloseWarningThread(cts.Token);
                closeWarningTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Exception ex = t.Exception;
                        ShowMessage("Something wrong with routine to close pop-up windows.");
                    }
                });
                // Create log file stream
                logger.StartLog(plnOpt.GetUserId());
                logger.WriteLog("Start batch processing.");
                // Start looping through the input plans
                ObservableCollection<PlanInput> dataGridColl = (ObservableCollection<PlanInput>)dataGridPlns.ItemsSource;
                int rowNumber = 0;
                foreach (PlanInput inPln in dataGridColl)
                {
                    rowNumber++;
                    if (inPln != null)
                    {
                        try
                        {
                            ExtPlan extPln = new ExtPlan(inPln);
                            if (dsCalcOnly)  // if dose calc only
                            {
                                ShowMessage("For row " + rowNumber + ", plan " + extPln.PlanId + ", computing dose.");
                                logger.WriteLog("For patient " + extPln.PatientId + ", course " + extPln.CourseId + ", plan " + extPln.PlanId + ", start dose computation.");
                                tsk = plnOpt.ComputeDose(extPln);
                                if (tsk.success)
                                {
                                    ShowMessage(tsk.message);
                                    logger.WriteLog(tsk.message);
                                    inPln.Stat = "✓";
                                }
                                else
                                {
                                    ShowMessage(tsk.message, Colors.Red);
                                    logger.WriteLog(tsk.message);
                                    inPln.Stat = "✕";
                                }
                            }
                            else  // if optimization
                            {
                                // First perform optimization
                                ShowMessage("For row " + rowNumber + ", plan " + extPln.PlanId + ", start optimization.");
                                logger.WriteLog("For patient " + extPln.PatientId + ", course " + extPln.CourseId + ", plan " + extPln.PlanId + ", start optimization.");
                                for (int idxRun = 1; idxRun < extPln.N_Runs + 1; idxRun++)
                                {
                                    ShowMessage("Running optimization iteration no. " + idxRun.ToString() + ".");
                                    logger.WriteLog("Start optimization run no." + idxRun.ToString() + " in plan \"" + extPln.PlanId + "\".");
                                    tsk = plnOpt.Optimize(extPln, idxRun);
                                    if (tsk.success)
                                    // If optimization successful, perform dose calc
                                    {
                                        logger.WriteLog(tsk.message);
                                        ShowMessage("Optimization iteration no." + idxRun.ToString() + " completed, computing dose.");
                                        logger.WriteLog("Start dose computation in plan \"" + extPln.PlanId + "\".");
                                        tsk2 = plnOpt.ComputeDose(extPln);
                                        if (tsk2.success)
                                        {
                                            ShowMessage(tsk2.message);
                                            logger.WriteLog(tsk2.message);
                                            inPln.Stat = "✓";
                                        }
                                        else
                                        {
                                            ShowMessage(tsk2.message, Colors.Red);
                                            logger.WriteLog(tsk2.message);
                                            inPln.Stat = "✕";
                                            break;  // exit "number of iterations" for loop
                                        }
                                    }
                                    else
                                    {
                                        ShowMessage(tsk.message, Colors.Red);
                                        logger.WriteLog(tsk.message);
                                        inPln.Stat = "✕";
                                        break;  // exit "number of iterations" for loop
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Windows.MessageBox.Show(ex.ToString());
                            ShowMessage("For row " + rowNumber + ", unexpected fault.", Colors.Red);
                        }
                    }
                }
                dataGridPlns.Items.Refresh();
                // Stop the close window thread
                cts.Cancel();
                cts.Dispose();
                ShowMessage("All batch process completed.");
                logger.WriteLog("All batch process completed.");
                // Close log file stream
                logger.EndLog();
                txtbStat.Text = "Ready.";
            }
            else
            {
                txtbStat.Text = "Cannot connect to database.";
            }
            // Re-enable all buttons
            Dispatcher.Invoke(() =>
            {
                dataGridPlns.IsReadOnly = false;
                btnAddPln.IsEnabled = true;
                btnRmvPln.IsEnabled = true;
                btnChckPln.IsEnabled = true;
                btnRunOpt.IsEnabled = true;
                chkBxDoseCalcOnly.IsEnabled = true;
            });
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
        }
    }
    internal class ExtPlan
    // Plan properties
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
    internal class PlanInput
    // Input plan row proerties
    {
        public string Pat { get; set; } = "";
        public string Crs { get; set; } = "";
        public string Pln { get; set; } = "";
        public int Iter { get; set; } = 1;
        public string Stat { get; set; } = "⨁";
    }
    internal class RunTask
    {
        public bool success { get; set; }
        public string message { get; set; }
        public RunTask() { }
        public RunTask(bool scs, string msg)
        {
            success = scs;
            message = msg;
        }
    }
}
