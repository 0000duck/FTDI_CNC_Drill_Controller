﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Deployment.Application;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace CNC_Drill_Controller1
{
    public partial class MainForm : Form
    {

        #region USB Interface Properties
        //oncomplete property : XCOPY "$(TargetDir)*.exe" "Z:\" /Y /I
        private IUSB_Controller USB = new USB_Control();
        private DateTime lastUIupdate;
        private int AxisOffsetCount;

        #endregion

        #region UI properties

        private bool CheckBoxInhibit;
        private DrillNode.DrillNodeStatus lastSelectedStatus;
        private int lastSelectedIndex;
        private char[] trimChars = { ' ' };
        private RawUSBForm RawUsbForm = new RawUSBForm();
        private TaskDialog taskDialog = new TaskDialog();

        #endregion

        #region View Properties

        private DrawingTypeDialog dtypeDialog = new DrawingTypeDialog();
        private const float NodeDiameter = 0.05f;
        private List<DrillNode> Nodes;
        private Viewer nodeViewer;
        private CrossHair cursorCrossHair;
        private CrossHair drillCrossHair;
        private Box CNCTableBox;
        private Box drawingPageBox;

        #endregion

        #region Async Worker, Callback and Thread Sync Properties

        private BackgroundWorker asyncWorker = new BackgroundWorker();

        //for BackgroundWorker
        private DoWorkEventHandler lastTask;
        private CleanupDelegate CleanupCallback;

        //for UI
        private ProgressDelegate ProgressCallback;
        private UpdateNodeDelegate UpdateNodeCallback;
        private AddLineDelegate AddLineCallback;

        #endregion

        #region Form Initialization

        public MainForm()
        {
            InitializeComponent();
            FormClosing += OnFormClosing;

            ProgressCallback = progressCallback;
            UpdateNodeCallback = updateNodeCallback;
            AddLineCallback = addLineCallback;

            asyncWorker.RunWorkerCompleted += asyncWorkerComplete;
            asyncWorker.ProgressChanged += asyncWorkerProgressChange;
            asyncWorker.WorkerReportsProgress = true;
            asyncWorker.WorkerSupportsCancellation = true;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            ExtLog.Logger = logger1;

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                Text += ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString(4);
            }
            else
            {
                Text += Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }

            #region View initialization

            cursorCrossHair = new CrossHair(0, 0, Color.Blue);
            drillCrossHair = new CrossHair(0, 0, Color.Red);
            CNCTableBox = new Box(0, 0, 6, 6, Color.LightGray);
            drawingPageBox = new Box(0, 0, 8.5f, 11, Color.GhostWhite);
            nodeViewer = new Viewer(OutputLabel, new PointF(11.0f, 11.0f));
            nodeViewer.OnSelect += OnSelect;
            lastSelectedStatus = DrillNode.DrillNodeStatus.Idle;
            lastSelectedIndex = -1;
            RebuildListBoxAndViewerFromNodes();

            #endregion

            #region UI Initialization

            AxisOffsetComboBox.SelectedIndex = 0;
            AxisOffsetCount = 1;
            XScaleTextBox.Text = GlobalProperties.X_Scale.ToString("D");
            YScaleTextBox.Text = GlobalProperties.Y_Scale.ToString("D");
            XBacklastTextbox.Text = GlobalProperties.X_Backlash.ToString("D");
            YBacklastTextbox.Text = GlobalProperties.Y_Backlash.ToString("D");

            #endregion

            #region USB interface initialization

            USBdevicesComboBox.Items.Clear();
            var USBDevices = USB.GetDevicesList();
            if (USBDevices.Count > 0)
            {
                foreach (var uDev in USBDevices)
                {
                    USBdevicesComboBox.Items.Add(uDev);
                }
                USBdevicesComboBox.SelectedIndex = 0;
            }
            else
            {
                USBdevicesComboBox.Items.Add("[None]");
                USB = new USB_Control_Emulator();
            }

            USB.OnProgress = OnProgress;

            USB.X_Abs_Location = GlobalProperties.X_Pos;
            USB.Y_Abs_Location = GlobalProperties.Y_Pos;
            USB.X_Delta = GlobalProperties.X_Delta;
            USB.Y_Delta = GlobalProperties.Y_Delta;
            USB.X_Last_Direction = GlobalProperties.X_Dir;
            USB.Y_Last_Direction = GlobalProperties.Y_Dir;

            USB.X_Driver = checkBoxX.Checked;
            USB.Y_Driver = checkBoxY.Checked;
            USB.T_Driver = checkBoxT.Checked;
            USB.Cycle_Drill = checkBoxD.Checked;

            USB.Inhibit_Backlash_Compensation = IgnoreBacklashBox.Checked;

            #endregion

        }

        private void USBdevicesComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var locStr = (string)USBdevicesComboBox.SelectedItem;
            locStr = locStr.Split(new[] { ':' })[0];
            uint loc;
            if (uint.TryParse(locStr, out loc)) USB.OpenDeviceByLocation(loc);
            else
            {
                logger1.AddLine("Failed to parse Location Id of: " + (string)USBdevicesComboBox.SelectedItem);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs formClosingEventArgs)
        {
            try
            {
                saveLogToolStripMenuItem_Click(sender, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error while saving logfile", MessageBoxButtons.OK);
            }

            GlobalProperties.SaveProperties();
        }

        #endregion

        #region Event Callback and Thread Sync Methods

        private void OnProgress(int progress, bool done)
        {
            Invoke(ProgressCallback, new object[] { progress, done });
        }
        private void progressCallback(int progress, bool done)
        {
            toolStripProgressBar.Value = progress;
            UIupdateTimer_Tick(this, null);
        }

        private void OnUpdateNode(int nodeIndex, DrillNode.DrillNodeStatus newStatus)
        {
            Invoke(UpdateNodeCallback, new object[] { nodeIndex, newStatus });
        }
        private void updateNodeCallback(int nodeIndex, DrillNode.DrillNodeStatus newStatus)
        {
            Nodes[nodeIndex].status = newStatus;
            UpdateNodeColors();
        }

        private void OnAddLine(string text)
        {
            Invoke(AddLineCallback, new object[] { text });
        }
        private void addLineCallback(string text)
        {
            logger1.AddLine(text);
        }



        #endregion

        #region Direct USB UI control methods

        private void PlusXbutton_Click(object sender, EventArgs e)
        {
            USB.MoveBy(AxisOffsetCount, 0);
        }
        private void MinusXbutton_Click(object sender, EventArgs e)
        {
            USB.MoveBy(-AxisOffsetCount, 0);
        }
        private void PlusYbutton_Click(object sender, EventArgs e)
        {
            USB.MoveBy(0, AxisOffsetCount);
        }
        private void MinusYbutton_Click(object sender, EventArgs e)
        {
            USB.MoveBy(0, -AxisOffsetCount);
        }
        private void checkBoxB_CheckedChanged(object sender, EventArgs e)
        {
            USB.X_Driver = checkBoxX.Checked;
            USB.Y_Driver = checkBoxY.Checked;
            USB.T_Driver = checkBoxT.Checked;
            USB.Cycle_Drill = checkBoxD.Checked;

            if (!CheckBoxInhibit) USB.Transfer();
        }
        private void Sendbutton_Click(object sender, EventArgs e)
        {
            USB.Transfer();
        }
        private void ReloadUSBbutton_Click(object sender, EventArgs e)
        {
            Form1_Load(this, e);
        }
        private void IgnoreBacklashBox_CheckedChanged(object sender, EventArgs e)
        {
            USB.Inhibit_Backlash_Compensation = IgnoreBacklashBox.Checked;
        }
        private void showRawCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            RawUsbForm.Visible = showRawCheckbox.Checked;
            if (RawUsbForm.Visible) RawUsbForm.Update(USB.InputBuffer);
        }

        private void setXButton_Click(object sender, EventArgs e)
        {
            // (loc - delta) / scale = pos
            // loc - delta = (pos * scale)
            // loc - (pos * scale) = delta
            USB.X_Delta = (int)(USB.X_Abs_Location - (TextConverter.SafeTextToFloat(XCurrentPosTextBox.Text) * GlobalProperties.X_Scale));
        }
        private void SetYButton_Click(object sender, EventArgs e)
        {
            USB.Y_Delta = (int)(USB.Y_Abs_Location - (TextConverter.SafeTextToFloat(YCurrentPosTextBox.Text) * GlobalProperties.Y_Scale));
        }

        private void SetAllButton_Click(object sender, EventArgs e)
        {
            setXButton_Click(sender, e);
            SetYButton_Click(sender, e);
        }

        private void zeroXbutton_Click(object sender, EventArgs e)
        {
            XCurrentPosTextBox.Text = "0.000";
            setXButton_Click(this, e);
        }
        private void zeroYbutton_Click(object sender, EventArgs e)
        {
            YCurrentPosTextBox.Text = "0.000";
            SetYButton_Click(this, e);
        }
        private void zeroAllbutton_Click(object sender, EventArgs e)
        {
            zeroXbutton_Click(this, e);
            zeroYbutton_Click(this, e);
        }
        private void AxisOffsetComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var toParse = (string)AxisOffsetComboBox.SelectedItem;
            toParse = toParse.Split(new[] { ' ' })[0];
            try
            {
                AxisOffsetCount = Convert.ToInt32(toParse);
            }
            catch
            {
                AxisOffsetCount = 1;
            }
        }
        private void AbortMoveButton_Click(object sender, EventArgs e)
        {
            USB.CancelMove();
        }

        #endregion

        #region Log methods
        private void clearLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            logger1.Clear();
        }

        private void saveLogToolStripMenuItem_Click(object sender, EventArgs e)
        {

            var logfile = (File.Exists(GlobalProperties.Logfile_Filename)) ? File.AppendText(GlobalProperties.Logfile_Filename) : File.CreateText(GlobalProperties.Logfile_Filename);
            logfile.WriteLine("Saving Log [" + DateTime.Now.ToString("F") + "]");
            logfile.Write(logger1.Text);
            logfile.WriteLine("");
            logfile.Close();
            logger1.AddLine("Log Saved to " + GlobalProperties.Logfile_Filename);
        }
        #endregion

        #region UI Update and Settings

        private PointF GetViewCursorLocation()
        {
            var snapLocation = nodeViewer.MousePositionF;
            if (SnapViewBox.Checked)
            {
                var snapSize = TextConverter.SafeTextToFloat(SnapSizeTextBox.Text);
                snapLocation.X = (float)Math.Round(snapLocation.X / snapSize) * snapSize;
                snapLocation.Y = (float)Math.Round(snapLocation.Y / snapSize) * snapSize;
            }

            return snapLocation;
        }

        private void XSetTransformButton_Click(object sender, EventArgs e)
        {
            GlobalProperties.X_Scale = TextConverter.SafeTextToInt(XScaleTextBox.Text);
            GlobalProperties.X_Backlash = TextConverter.SafeTextToInt(XBacklastTextbox.Text);
            logger1.AddLine("Set X Axis Scale to: " + GlobalProperties.X_Scale + " steps/inch, Backlash to: " + GlobalProperties.X_Backlash + "steps.");
        }

        private void YSetTransformButton_Click(object sender, EventArgs e)
        {
            GlobalProperties.Y_Scale = TextConverter.SafeTextToInt(YScaleTextBox.Text);
            GlobalProperties.Y_Backlash = TextConverter.SafeTextToInt(YBacklastTextbox.Text);
            logger1.AddLine("Set Y Axis Scale to: " + GlobalProperties.Y_Scale + " steps/inch, Backlash to: " + GlobalProperties.Y_Backlash + "steps.");
        }

        private void UIupdateTimer_Tick(object sender, EventArgs e)
        {
            #region UI update
            if (USB.IsOpen)
            {
                CheckBoxInhibit = true;
                //fetch data if too old
                if ((DateTime.Now.Subtract(USB.LastUpdate)).Milliseconds > GlobalProperties.USB_Refresh_Period)
                {
                    USB.Transfer();
                }

                if (RawUsbForm.Visible) RawUsbForm.Update(USB.InputBuffer);

                if (USB.MinXswitch && USB.MaxXswitch) //check for impossible combinaison (step controller or power not plugged-in)
                {
                    XMinStatusLabel.BackColor = Color.DodgerBlue;
                    XMaxStatusLabel.BackColor = Color.DodgerBlue;
                }
                else
                {
                    XMinStatusLabel.BackColor = !USB.MinXswitch ? Color.Lime : Color.Red;
                    XMaxStatusLabel.BackColor = !USB.MaxXswitch ? Color.Lime : Color.Red;
                }

                if (USB.MinYswitch && USB.MaxYswitch)
                {
                    YMinStatusLabel.BackColor = Color.DodgerBlue;
                    YMaxStatusLabel.BackColor = Color.DodgerBlue;
                }
                else
                {
                    YMinStatusLabel.BackColor = !USB.MinYswitch ? Color.Lime : Color.Red;
                    YMaxStatusLabel.BackColor = !USB.MaxYswitch ? Color.Lime : Color.Red;
                }

                TopStatusLabel.BackColor = USB.TopSwitch ? Color.Lime : SystemColors.Control;
                BottomStatusLabel.BackColor = USB.BottomSwitch ? Color.Lime : SystemColors.Control;

                //reset drill cycle
                if (checkBoxD.Checked && !USB.TopSwitch && !USB.BottomSwitch)
                {
                    checkBoxD.Checked = false;
                }

                CheckBoxInhibit = false;
            }
            #endregion

            #region View update

            XStatusLabel.Text = USB.X_Rel_Location.ToString("D5");
            YStatusLabel.Text = USB.Y_Rel_Location.ToString("D5");

            var curLoc = USB.CurrentLocation();
            Xlabel.Text = "X: " + curLoc.X.ToString("F3");
            Ylabel.Text = "Y: " + curLoc.Y.ToString("F3");

            var snapLocation = GetViewCursorLocation();
            cursorCrossHair.UpdatePosition(snapLocation);
            ViewXLabel.Text = snapLocation.X.ToString("F3");
            ViewYLabel.Text = snapLocation.Y.ToString("F3");
            ViewZoomLabel.Text = (int)(nodeViewer.ZoomLevel * 100) + "%";

            drillCrossHair.UpdatePosition(curLoc.X, curLoc.Y);

            #endregion

            #region Refresh required elements

            if ((DateTime.Now.Subtract(lastUIupdate)).Milliseconds > GlobalProperties.Label_Refresh_Period)
            {

                OutputLabel.Refresh();
                Xlabel.Refresh();
                Ylabel.Refresh();
                statusStrip1.Refresh();
                logger1.Refresh();

                lastUIupdate = DateTime.Now;

                Application.DoEvents();
            }

            #endregion

            #region Backup state

            GlobalProperties.X_Dir = USB.X_Last_Direction;
            GlobalProperties.Y_Dir = USB.Y_Last_Direction;

            GlobalProperties.X_Pos = USB.X_Abs_Location;
            GlobalProperties.Y_Pos = USB.Y_Abs_Location;

            GlobalProperties.X_Delta = USB.X_Delta;
            GlobalProperties.Y_Delta = USB.Y_Delta;

            if ((DateTime.Now.Subtract(GlobalProperties.LastSave)).Milliseconds > GlobalProperties.GlobalProperties_Refresh_Period)
            {
                GlobalProperties.SaveProperties();
            }

            #endregion
        }

        #endregion

        #region View / Listbox UI controls
        private void OutputLabel_MouseEnter(object sender, EventArgs e)
        {
            Cursor.Hide();
        }

        private void OutputLabel_MouseLeave(object sender, EventArgs e)
        {
            Cursor.Show();
        }

        private void OnSelect(List<IViewerElements> selection)
        {
            if ((Nodes != null) && (Nodes.Count > 0))
                for (var i = 0; i < selection.Count; i++)
                {
                    for (var j = 0; j < Nodes.Count; j++)
                    {
                        if (selection[i].ID == Nodes[j].ID)
                        {
                            listBox1.SelectedIndex = Nodes[j].ID;
                        }
                    }
                }
        }

        private void LoadFileButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.FileName = "*.vdx";
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {

                if (dtypeDialog.ShowDialog() == DialogResult.OK)
                {
                    logger1.AddLine("Opening File: " + openFileDialog1.FileName);
                    logger1.AddLine("Inverted: " + dtypeDialog.DrawingConfig.Inverted);
                    logger1.AddLine("Type: " + dtypeDialog.DrawingConfig.Type);

                    var loader = new VDXLoader(openFileDialog1.FileName, dtypeDialog.DrawingConfig.Inverted);
                    Nodes = loader.DrillNodes;
                    logger1.AddLine(Nodes.Count.ToString("D") + " Nodes loaded.");

                    drawingPageBox = new Box(0, 0, loader.PageWidth, loader.PageHeight, Color.GhostWhite);
                    lastSelectedStatus = DrillNode.DrillNodeStatus.Idle;
                    RebuildListBoxAndViewerFromNodes();
                    lastSelectedIndex = -1;
                }
            }
        }

        private void MoveTobutton_Click(object sender, EventArgs e)
        {
            var movedata = (string)listBox1.SelectedItem;

            var axisdata = movedata.Split(trimChars);
            if (axisdata.Length == 2)
            {
                var mx = TextConverter.SafeTextToFloat(axisdata[0].Trim(trimChars));
                var my = TextConverter.SafeTextToFloat(axisdata[1].Trim(trimChars));
                logger1.AddLine("Moving to: " + mx.ToString("F3") + ", " + my.ToString("F3"));
                USB.MoveTo(mx, my);
            }
        }
        private void SetAsXYbutton_Click(object sender, EventArgs e)
        {
            var movedata = (string)listBox1.SelectedItem;
            var axisdata = movedata.Split(trimChars);
            if (axisdata.Length == 2)
            {
                XCurrentPosTextBox.Text = axisdata[0].Trim(trimChars);
                YCurrentPosTextBox.Text = axisdata[1].Trim(trimChars);
                setXButton_Click(this, e);
                SetYButton_Click(this, e);
            }
        }
        private void listBox1_DoubleClick(object sender, EventArgs e)
        {
            NodesContextMenu.Show(listBox1, listBox1.PointToClient(Cursor.Position));
        }
        private void NodeContextTARGET_Click(object sender, EventArgs e)
        {
            Nodes[listBox1.SelectedIndex].status = DrillNode.DrillNodeStatus.Next;
            UpdateNodeColors();
        }
        private void NodeContextDRILED_Click(object sender, EventArgs e)
        {
            Nodes[listBox1.SelectedIndex].status = DrillNode.DrillNodeStatus.Drilled;
            UpdateNodeColors();
        }
        private void NodeContextIDLE_Click(object sender, EventArgs e)
        {
            Nodes[listBox1.SelectedIndex].status = DrillNode.DrillNodeStatus.Idle;
            UpdateNodeColors();
        }

        private void UpdateNodeColors()
        {
            for (var i = 0; i < Nodes.Count; i++)
            {
                for (var j = 0; j < nodeViewer.Elements.Count; j++)
                {
                    if (nodeViewer.Elements[j].ID == Nodes[i].ID)
                        nodeViewer.Elements[j].color = Nodes[i].Color;
                }
            }
        }

        private void RebuildListBoxAndViewerFromNodes()
        {
            listBox1.Items.Clear();
            lastSelectedStatus = DrillNode.DrillNodeStatus.Idle;
            lastSelectedIndex = -1;
            nodeViewer.Elements = new List<IViewerElements>
            {
                drawingPageBox,
                CNCTableBox,
                drillCrossHair,
                cursorCrossHair
            };

            if ((Nodes != null) && (Nodes.Count > 0))
            {
                for (var i = 0; i < Nodes.Count; i++)
                {
                    nodeViewer.Elements.Add(new Node(Nodes[i].location, NodeDiameter, Nodes[i].Color, i));
                    listBox1.Items.Add(Nodes[i].Location);
                    Nodes[i].ID = i;
                }
            }
        }

        private void OffsetOriginBtton_Click(object sender, EventArgs e)
        {
            var origOffset = new SizeF(TextConverter.SafeTextToFloat(XoriginTextbox.Text), TextConverter.SafeTextToFloat(YOriginTextbox.Text));
            for (var i = 0; i < Nodes.Count; i++)
                Nodes[i].location = new PointF(Nodes[i].location.X - origOffset.Width, Nodes[i].location.Y - origOffset.Height);
            RebuildListBoxAndViewerFromNodes();
            XoriginTextbox.Text = "0.000";
            YOriginTextbox.Text = "0.000";
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lastSelectedIndex >= 0)
            {
                if (Nodes[lastSelectedIndex].status == DrillNode.DrillNodeStatus.Selected)
                    Nodes[lastSelectedIndex].status = lastSelectedStatus;
            }
            lastSelectedStatus = Nodes[listBox1.SelectedIndex].status;
            Nodes[listBox1.SelectedIndex].status = DrillNode.DrillNodeStatus.Selected;
            lastSelectedIndex = listBox1.SelectedIndex;
            UpdateNodeColors();
            OutputLabel.Refresh();
        }
        #endregion

        #region View / Output Label Context Menu Methods
        private void OutputLabel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ViewContextMenu.Show(OutputLabel, OutputLabel.PointToClient(Cursor.Position));
            }
        }
        private void ViewSetDRGOrigin_Click(object sender, EventArgs e)
        {
            var origOffset = GetViewCursorLocation();
            for (var i = 0; i < Nodes.Count; i++)
                Nodes[i].location = new PointF(Nodes[i].location.X - origOffset.X, Nodes[i].location.Y - origOffset.Y);
            RebuildListBoxAndViewerFromNodes();
            XoriginTextbox.Text = "0.000";
            YOriginTextbox.Text = "0.000";
        }
        private void ViewSetXYContext_Click(object sender, EventArgs e)
        {
            var newLocation = GetViewCursorLocation();
            XCurrentPosTextBox.Text = newLocation.X.ToString("F3");
            setXButton_Click(sender, e);
            YCurrentPosTextBox.Text = newLocation.Y.ToString("F3");
            SetYButton_Click(sender, e);
        }
        private void moveToToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var targetLocation = GetViewCursorLocation();
            logger1.AddLine("Moving to: " + targetLocation.X.ToString("F3") + ", " + targetLocation.Y.ToString("F3"));
            USB.MoveTo(targetLocation.X, targetLocation.Y);
        }
        #endregion

        #region Path and Control helpers

        private void OptimizeButton_Click(object sender, EventArgs e)
        {
            var old_length = DrillNodeHelper.getPathLength(Nodes, USB.CurrentLocation());

            var NodesNN = DrillNodeHelper.OptimizeNodesNN(Nodes, USB.CurrentLocation());
            var NN_Length = DrillNodeHelper.getPathLength(NodesNN, USB.CurrentLocation());

            var NodesHSL = DrillNodeHelper.OptimizeNodesHScanLine(Nodes, new PointF(0, 0));
            var HSL_Length = DrillNodeHelper.getPathLength(NodesHSL, USB.CurrentLocation());

            var NodesVSL = DrillNodeHelper.OptimizeNodesVScanLine(Nodes, new PointF(0, 0));
            var VSL_Length = DrillNodeHelper.getPathLength(NodesVSL, USB.CurrentLocation());

            var best_SL_length = (VSL_Length < HSL_Length) ? VSL_Length : HSL_Length;
            var best_SL_path = (VSL_Length < HSL_Length) ? NodesVSL : NodesHSL;

            var best_length = (NN_Length < best_SL_length) ? NN_Length : best_SL_length;
            var best_path = (NN_Length < best_SL_length) ? NodesNN : best_SL_path;

            if (best_length < old_length)
            {
                Nodes = best_path;
            }

            else logger1.AddLine("Optimization test returned path longer or equal.");

            RebuildListBoxAndViewerFromNodes();
        }

        #endregion

        #region Scripted Methods

        #region Async Tasks Handlers

        private void abortAsyncWorker()
        {
            if (asyncWorker.IsBusy)
            {
                logger1.AddLine("Cancelling Task...");
                asyncWorker.CancelAsync();
            }
        }

        private void startAsyncWorkerWithTask(string desc, DoWorkEventHandler asyncWork, CleanupDelegate asyncCleanup, object argument)
        {
            if (!asyncWorker.IsBusy)
            {
                if (USB.Check_Limit_Switches() && USB.IsOpen)
                {
                    logger1.AddLine("Starting Async Task");

                    USB.Inhibit_LimitSwitches_Warning = true;
                    Enabled = false;

                    try
                    {
                        logger1.AddLine(desc);
                        asyncWorker.DoWork += asyncWork;
                        lastTask = asyncWork;
                        CleanupCallback = asyncCleanup;

                        asyncWorker.RunWorkerAsync(argument);
                    }
                    catch (Exception ex)
                    {
                        logger1.AddLine("Async Task failed: " + ex.Message);
                    }

                    logger1.AddLine("Async Started");

                    if (taskDialog.ShowDialog(this) == DialogResult.Abort)
                    {
                        abortAsyncWorker();
                        USB.CancelMove();
                    }
                    Enabled = true;
                    USB.Inhibit_LimitSwitches_Warning = false;
                }
                else logger1.AddLine("Can't init scripted sequence, limit switches are not properly set or USB interface is Closed");
            }
            else logger1.AddLine("Async Task Already Running");
        }

        private void asyncWorkerProgressChange(object sender, ProgressChangedEventArgs e)
        {
            var inhibit = e.UserState as bool? ?? false;
            if (!inhibit) logger1.AddLine("Progress: " + e.ProgressPercentage.ToString("D") + "%");
            taskDialog.update(e.ProgressPercentage);
        }

        private void asyncWorkerComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            asyncWorker.DoWork -= lastTask;
            taskDialog.done();
            if (e.Error != null)
            {
                logger1.AddLine("Error running Task: " + e.Error.Message);
            }
            else if (e.Cancelled)
            {
                logger1.AddLine("Task Cancelled");
            }
            else
            {
                var success = e.Result as bool? ?? false;
                if (CleanupCallback != null) CleanupCallback(success);
            }
        }

        #endregion

        #region Async Task Helpers

        private bool Initiate_Drill_From_Top(int numTries, int TriesPeriod)
        {
            var success = true;
            USB.Cycle_Drill = true;

            while (USB.TopSwitch && success)
            {
                USB.Transfer();
                success = USB.IsOpen && (numTries >= 0);
                numTries--;
                Thread.Sleep(TriesPeriod);
            }
            return success;
        }
        private bool Wait_For_Drill_To_Top(int numTries, int TriesPeriod)
        {
            var success = true;
            USB.Cycle_Drill = false;

            while (!USB.TopSwitch && success)
            {
                USB.Transfer();
                success = !USB.IsOpen || (numTries >= 0);
                numTries--;
                Thread.Sleep(TriesPeriod);
            }
            return success;
        }

        private bool SeekXminSwitch(bool AxisDirection, int byX, int byY, int TriesPeriod)
        {
            var success = true;
            var maxTries = GlobalProperties.numStepsPerTurns;
            while ((USB.MinXswitch == AxisDirection) && USB.IsOpen && (maxTries > 0))
            {
                USB.MoveBy(byX, byY);
                maxTries--;
                Thread.Sleep(TriesPeriod);
                USB.Transfer();
                success = maxTries >= 0;
            }
            return success;
        }
        private bool SeekYminSwitch(bool AxisDirection, int byX, int byY, int TriesPeriod)
        {
            var success = true;
            var maxTries = GlobalProperties.numStepsPerTurns;
            while ((USB.MinYswitch == AxisDirection) && USB.IsOpen && (maxTries > 0))
            {
                USB.MoveBy(byX, byY);
                maxTries--;
                Thread.Sleep(TriesPeriod);
                USB.Transfer();
                success = maxTries >= 0;
            }
            return success;
        }

        #endregion

        #region Async Task: Drill Selected Node

        private void AsyncDrillSelectedButton_Click(object sender, EventArgs e)
        {
            if ((listBox1.SelectedIndex > 0) && (listBox1.SelectedIndex <= Nodes.Count))
            {
                Nodes[listBox1.SelectedIndex].status = DrillNode.DrillNodeStatus.Next;
                UpdateNodeColors();

                var toDrill = Nodes[listBox1.SelectedIndex].location;

                startAsyncWorkerWithTask("Drill Selected Node (Async)...",
                    asyncWorkerDoWork_DrillSelected, asyncWorkerDoWork_DrillSelected_Cleanup, toDrill);

            }
            else logger1.AddLine("Invalid Selection");
        }
        private void asyncWorkerDoWork_DrillSelected(object sender, DoWorkEventArgs doWorkEventArgs)
        {
            var loc = doWorkEventArgs.Argument as PointF? ?? PointF.Empty;

            USB.MoveTo(loc.X, loc.Y);

            var success = USB.Check_Limit_Switches();
            asyncWorker.ReportProgress(50);

            //start drill from top
            if (!asyncWorker.CancellationPending)
            {
                if (success && USB.IsOpen && USB.Check_Limit_Switches())
                {
                    success = Initiate_Drill_From_Top(20, 50);
                }
                asyncWorker.ReportProgress(75);
            }
            else doWorkEventArgs.Cancel = true;

            //wait for drill to reach back top
            if (!asyncWorker.CancellationPending)
            {
                if (success && USB.IsOpen && USB.Check_Limit_Switches())
                {
                    success = Wait_For_Drill_To_Top(20, 50);
                }
                asyncWorker.ReportProgress(100);
            }
            else doWorkEventArgs.Cancel = true;

            doWorkEventArgs.Result = success;
        }
        private void asyncWorkerDoWork_DrillSelected_Cleanup(bool success)
        {
            if (success)
            {
                logger1.AddLine("Task Completed");
                Nodes[listBox1.SelectedIndex].status = DrillNode.DrillNodeStatus.Drilled;
                UpdateNodeColors();
            }
            else logger1.AddLine("Drill Sequence Failed");
        }

        #endregion

        #region Async Task: Find Axis Origin

        private void AsyncStartFindOriginButton_Click(object sender, EventArgs e)
        {
            startAsyncWorkerWithTask("Seeking Axis Origins (Async)...",
                asyncWorkerDoWork_FindAxisOrigin,
                asyncWorkerDoWork_FindAxisOrigin_Cleanup, null);
        }
        private void GetCloserToOrigin()
        {
            var delta_X = (USB.X_Rel_Location > GlobalProperties.X_Scale) ? USB.X_Rel_Location - GlobalProperties.X_Scale : 0;
            var delta_Y = (USB.Y_Rel_Location > GlobalProperties.Y_Scale) ? USB.Y_Rel_Location - GlobalProperties.Y_Scale : 0;
            USB.MoveBy(-delta_X, -delta_Y);
        }

        private void asyncWorkerDoWork_FindAxisOrigin(object sender, DoWorkEventArgs doWorkEventArgs)
        {
            GetCloserToOrigin();

            var success = USB.Check_Limit_Switches();
            asyncWorker.ReportProgress(30);

            if (!asyncWorker.CancellationPending)
            {
                success = SeekXminSwitch(false, -30, 0, 10); //1.5in
                asyncWorker.ReportProgress(45);
            }
            else doWorkEventArgs.Cancel = true;

            if (!asyncWorker.CancellationPending)
            {
                if (success) success = SeekXminSwitch(true, 1, 0, 50); //1 turn
                asyncWorker.ReportProgress(60);
            }
            else doWorkEventArgs.Cancel = true;

            if (!asyncWorker.CancellationPending)
            {
                if (success) success = SeekYminSwitch(false, 0, -30, 10);
                asyncWorker.ReportProgress(75);
            }
            else doWorkEventArgs.Cancel = true;

            if (!asyncWorker.CancellationPending)
            {
                if (success) success = SeekYminSwitch(true, 0, 1, 50);
                asyncWorker.ReportProgress(90);
            }
            else doWorkEventArgs.Cancel = true;

            if (!asyncWorker.CancellationPending)
            {
                asyncWorker.ReportProgress(100);
            }
            else doWorkEventArgs.Cancel = true;

            doWorkEventArgs.Result = success;
        }
        private void asyncWorkerDoWork_FindAxisOrigin_Cleanup(bool success)
        {
            logger1.AddLine("Task Completed");
            if (success)
            {
                var loc = USB.CurrentLocation();
                logger1.AddLine("Location Set to Zero, Origin was found at X=" + loc.X.ToString("F3") + " Y=" + loc.Y.ToString("F3"));
                zeroAllbutton_Click(this, null);
            }
            else logger1.AddLine("Origin not found (out of reach / farther than 1 inch from expected location)");
        }
        #endregion

        #region Async Task: Drill All Nodes

        private void DrillAllNodebutton_Click(object sender, EventArgs e)
        {
            if ((Nodes != null) && (Nodes.Count > 0))
            {
                startAsyncWorkerWithTask("Drill All Nodes (Async)...",
                    asyncWorkerDoWork_DrillAll, asyncWorkerDoWork_DrillAll_Cleanup, Nodes);
            }
            else logger1.AddLine("No Nodes to Drill");
        }

        private void asyncWorkerDoWork_DrillAll(object sender, DoWorkEventArgs e)
        {
            var nodes = e.Argument as List<DrillNode> ?? new List<DrillNode>();
            var success = nodes.Count > 0;

            if (success) for (var i = 0; i < nodes.Count; i++)
                {
                    if (!asyncWorker.CancellationPending)
                    {
                        if (success && USB.Check_Limit_Switches() && USB.TopSwitch && !USB.BottomSwitch)
                        {
                            if (nodes[i].status != DrillNode.DrillNodeStatus.Drilled)
                            {

                                OnUpdateNode(i, DrillNode.DrillNodeStatus.Next);

                                OnAddLine("Moving to [" + (i + 1) + "/" + nodes.Count + "]: " + nodes[i].Location);
                                USB.MoveTo(Nodes[i].location.X, Nodes[i].location.Y);

                                OnAddLine("Drilling...");

                                //start drill from top
                                if (USB.IsOpen && USB.Check_Limit_Switches())
                                {
                                    success = Initiate_Drill_From_Top(20, 50);
                                }

                                //wait for drill to reach back top
                                if (success && USB.IsOpen && USB.Check_Limit_Switches())
                                {
                                    success = Wait_For_Drill_To_Top(20, 50);
                                }

                                OnUpdateNode(i, DrillNode.DrillNodeStatus.Drilled);
                                asyncWorker.ReportProgress(100 * (i + 1) / nodes.Count, true);
                            }
                            else
                            {
                                OnAddLine("Node [" + (i + 1) + "/" + nodes.Count + "] already drilled");
                            }
                        }
                        else
                        {
                            success = false;
                        }
                    }
                    else e.Cancel = true;
                }

            asyncWorker.ReportProgress(100);
            e.Result = success;
        }

        private void asyncWorkerDoWork_DrillAll_Cleanup(bool success)
        {
            logger1.AddLine(success ? "Task Completed" : "Drill Sequence Failed");
        }

        #endregion

        #endregion
    }
}
