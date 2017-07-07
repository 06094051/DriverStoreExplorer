﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Rapr.Core;
using Rapr.Utils;

namespace Rapr
{
    public partial class DSEForm : Form
    {
        IDriverStore driverStore;
        Color SavedBackColor, SavedForeColor;
        OperationContext context = new OperationContext();

        public DSEForm()
        {
            InitializeComponent();

            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            lstDriverStoreEntries.PrimarySortColumn = this.driverClassColumn;
            lstDriverStoreEntries.PrimarySortOrder = SortOrder.Ascending;
            lstDriverStoreEntries.SecondarySortColumn = this.driverDateColumn;
            lstDriverStoreEntries.SecondarySortOrder = SortOrder.Descending;
            lstDriverStoreEntries.CheckBoxes = isRunAsAdministrator;
            driverSizeColumn.AspectToStringConverter = size => DriverStoreEntry.GetBytesReadable((long)size);

            this.driverVersionColumn.GroupKeyGetter = delegate (object rowObject)
            {
                DriverStoreEntry driver = (DriverStoreEntry)rowObject;
                return new Version(driver.DriverVersion.Major, driver.DriverVersion.Minor);
            };

            this.driverDateColumn.GroupKeyGetter = delegate (object rowObject)
            {
                DriverStoreEntry driver = (DriverStoreEntry)rowObject;
                return new DateTime(driver.DriverDate.Year, driver.DriverDate.Month, 1);
            };

            this.driverDateColumn.GroupKeyToTitleConverter = delegate (object groupKey)
            {
                return ((DateTime)groupKey).ToString("yyyy-MM");
            };

            this.driverSizeColumn.GroupKeyGetter = delegate (object rowObject)
            {
                DriverStoreEntry driver = (DriverStoreEntry)rowObject;
                return DriverStoreEntry.GetSizeRange(driver.DriverSize);
            };

            this.driverSizeColumn.GroupKeyToTitleConverter = delegate (object groupKey)
            {
                return DriverStoreEntry.GetSizeRangeName((long)groupKey);
            };

            Trace.TraceInformation("---------------------------------------------------------------");
            Trace.TraceInformation($"{Application.ProductName} started");

            driverStore = new PNPUtil();

            if (!IsOSSupported())
            {
                MessageBox.Show("This utility cannot be run in pre-Vista OS", "Rapr", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Application.Exit();
            }
        }

        private void DSEForm_Shown(object sender, EventArgs e)
        {
            SavedBackColor = lblStatus.BackColor;
            SavedForeColor = lblStatus.ForeColor;

            if (!isRunAsAdministrator)
            {
                Text = Text + " [Read-Only Mode]";
                ShowStatus("Running in Read-Only mode", Status.Warning);
                buttonAddDriver.Enabled = false;
                cbAddInstall.Enabled = false;
                buttonDeleteDriver.Enabled = false;
                cbForceDeletion.Enabled = false;
                buttonSelectOldDrivers.Enabled = false;
                labelRunAsAdmin.Visible = true;
                buttonRunAsAdmin.Visible = true;
            }

            PopulateUIWithDriverStoreEntries();
        }

        private void DSEForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            Trace.TraceInformation($"Shutting down - reason {e.CloseReason}");
        }

        private void buttonEnumerate_Click(object sender, EventArgs e)
        {
            PopulateUIWithDriverStoreEntries();
        }

        private void buttonDelete_Click(object sender, EventArgs e)
        {
            if (lstDriverStoreEntries.CheckedObjects.Count == 0 && lstDriverStoreEntries.SelectedIndex == -1)
            {
                // No entry is selected 
                ShowStatus("Select a driver entry first", Status.Warning);
                return;
            }

            List<DriverStoreEntry> driverStoreEntries = new List<DriverStoreEntry>();
            if (lstDriverStoreEntries.CheckedObjects.Count == 0)
            {
                foreach (DriverStoreEntry o in lstDriverStoreEntries.SelectedObjects)
                {
                    driverStoreEntries.Add(o);
                }
            }
            else if (lstDriverStoreEntries.CheckedItems.Count > 0)
            {
                foreach (DriverStoreEntry o in lstDriverStoreEntries.CheckedObjects)
                {
                    driverStoreEntries.Add(o);
                }
            }

            DeleteDriverStoreEntries(driverStoreEntries);
        }

        private void DeleteDriverStoreEntries(List<DriverStoreEntry> driverStoreEntries)
        {
            string msgWarning;

            if (driverStoreEntries != null && driverStoreEntries.Count > 0)
            {
                if (driverStoreEntries.Count == 1)
                {
                    msgWarning = $"About to {(cbForceDeletion.Checked ? "force delete" : "delete")} {driverStoreEntries[0].DriverPublishedName} from driver store.{Environment.NewLine}Are you sure?";
                }
                else
                {
                    msgWarning = $"About to {(cbForceDeletion.Checked ? "force delete" : "delete")} {driverStoreEntries.Count} packages from driver store.{Environment.NewLine}Are you sure?";
                }

                if (DialogResult.OK == MessageBox.Show(
                    msgWarning,
                    "Warning",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning))
                {
                    DeleteDriverPackages(driverStoreEntries);
                }
            }
        }

        private void buttonAddDriver_Click(object sender, EventArgs e)
        {
            DialogResult dr = openFileDialog.ShowDialog();
            if (dr == DialogResult.OK)
            {
                string pkgFolder = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
                string infName = System.IO.Path.GetFileName(openFileDialog.FileName);

                AddDriverPackage(openFileDialog.FileName);
            }
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            OperationContext localContext = (OperationContext)e.Argument;

            switch (localContext.Code)
            {
                case OperationCode.EnumerateStore:
                    localContext.ResultData = driverStore.EnumeratePackages();
                    break;

                case OperationCode.DeleteDriver:
                    DeleteDriver(ref localContext, false);
                    break;

                case OperationCode.ForceDeleteDriver:
                    DeleteDriver(ref localContext, true);
                    break;

                case OperationCode.AddDriver:
                    localContext.ResultStatus = driverStore.AddPackage(localContext.InfPath, false);
                    break;

                case OperationCode.AddInstallDriver:
                    localContext.ResultStatus = driverStore.AddPackage(localContext.InfPath, true);
                    break;

                case OperationCode.Dummy:
                    throw new Exception("Invalid argument rcvd by bgroundWorker");
            }

            e.Result = localContext;
        }

        private void DeleteDriver(ref OperationContext localContext, bool force)
        {
            if (localContext.DriverStoreEntries != null)
            {
                bool totalResult = true;
                StringBuilder sb = new StringBuilder();

                if (localContext.DriverStoreEntries.Count == 1)
                {
                    localContext.ResultStatus = driverStore.DeletePackage(localContext.DriverStoreEntries[0], force);
                }
                else
                {
                    foreach (DriverStoreEntry dse in localContext.DriverStoreEntries)
                    {
                        bool result = driverStore.DeletePackage(dse, force);
                        string resultTxt = $"Delete {dse.DriverPublishedName} {(result ? "succeeded." : "failed.")}";
                        Trace.TraceInformation(resultTxt);

                        sb.AppendLine(resultTxt);
                        totalResult &= result;
                    }

                    localContext.ResultStatus = totalResult;
                    localContext.ResultData = sb.ToString();
                }
            }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            OperationContext localContext = (OperationContext)e.Result;
            string result;

            switch (localContext.Code)
            {
                case OperationCode.EnumerateStore:
                    List<DriverStoreEntry> ldse = localContext.ResultData as List<DriverStoreEntry>;
                    lstDriverStoreEntries.SetObjects(ldse);
                    lstDriverStoreEntries.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
                    break;

                case OperationCode.ForceDeleteDriver:
                case OperationCode.DeleteDriver:
                    if ((bool)localContext.ResultStatus)
                    {
                        if (localContext.DriverStoreEntries.Count == 1)
                        {
                            result = $"Removed the package {localContext.DriverStoreEntries[0].DriverPublishedName} from driver store";
                        }
                        else
                        {
                            result = $"Removed {localContext.DriverStoreEntries.Count} packages from driver store";
                        }

                        // refresh the UI
                        PopulateUIWithDriverStoreEntries();

                        ShowStatus(result, Status.Success);
                    }
                    else
                    {
                        string driverDeleteTip = localContext.Code == OperationCode.DeleteDriver
                            ? " [TIP: The driver may still being used. Try FORCE deleting the package]"
                            : string.Empty;

                        if (localContext.DriverStoreEntries.Count == 1)
                        {
                            result = $"Error removing the package {localContext.DriverStoreEntries[0].DriverPublishedName} from driver store{driverDeleteTip}";
                        }
                        else
                        {
                            result = $"Error removing some packages from driver store{driverDeleteTip}";
                            string fullResult = $"{result}{Environment.NewLine}{localContext.ResultData as string}";

                            // refresh the UI
                            PopulateUIWithDriverStoreEntries();

                            MessageBox.Show(
                                fullResult,
                                "Detailed Error Log",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }

                        ShowStatus(result, Status.Error);
                    }

                    cbForceDeletion.Checked = false;

                    break;

                case OperationCode.AddDriver:
                case OperationCode.AddInstallDriver:
                    if ((bool)localContext.ResultStatus == true)
                    {
                        result = $"Added{(localContext.Code == OperationCode.AddInstallDriver ? " & installed " : "")} the package {localContext.InfPath} to driver store";

                        // refresh the UI
                        PopulateUIWithDriverStoreEntries();
                        ShowStatus(result, Status.Success);
                    }
                    else
                    {
                        result = $"Error adding{(localContext.Code == OperationCode.AddInstallDriver ? " & installing " : "")} the package {localContext.InfPath} to driver store";

                        ShowStatus(result, Status.Error);
                    }

                    cbAddInstall.Checked = false;
                    break;
            }

            ShowOperationInProgress(false);
        }

        private static void ShowAboutBox()
        {
            using (AboutBox ab = new AboutBox())
            {
                ab.ShowDialog();
            }
        }
        private void linkAbout_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ShowAboutBox();
        }

        private void contextMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            // Check if there are any entries
            if ((lstDriverStoreEntries.Objects != null))
            {
                ctxMenuSelectAll.Enabled = isRunAsAdministrator;
                ctxMenuSelectOldDrivers.Enabled = isRunAsAdministrator;
                ctxMenuExport.Enabled = true;

                if (lstDriverStoreEntries.CheckedObjects != null && lstDriverStoreEntries.CheckedObjects.Count > 0)
                {
                    ctxMenuSelectAll.Text = "Unselect All";
                }
                else
                {
                    ctxMenuSelectAll.Text = "Select All";
                }

                if (lstDriverStoreEntries.SelectedObjects != null && lstDriverStoreEntries.SelectedObjects.Count > 0)
                {
                    ctxMenuDelete.Enabled = isRunAsAdministrator;

                    if (lstDriverStoreEntries.CheckedObjects != null
                        && lstDriverStoreEntries.CheckedObjects.Count > 0
                        && new ArrayList(lstDriverStoreEntries.SelectedObjects).ToArray().All(i => lstDriverStoreEntries.CheckedObjects.Contains(i)))
                    {
                        ctxMenuSelect.Text = "Unselect";
                    }
                    else
                    {
                        ctxMenuSelect.Text = "Select";
                    }

                    ctxMenuSelect.Enabled = isRunAsAdministrator;
                }
                else
                {
                    ctxMenuDelete.Enabled = false;
                    ctxMenuSelect.Enabled = false;
                }
            }
            else
            {
                ctxMenuSelect.Enabled = false;
                ctxMenuSelectAll.Enabled = false;
                ctxMenuSelectOldDrivers.Enabled = false;
                ctxMenuDelete.Enabled = false;
                ctxMenuExport.Enabled = false;
            }
        }

        // Function to switch between "selected" and "unselected" states
        private void ctxMenuSelectAll_Click(object sender, EventArgs e)
        {
            // Check if there are any entries
            if ((lstDriverStoreEntries.Objects != null))
            {
                if (lstDriverStoreEntries.CheckedObjects != null && lstDriverStoreEntries.CheckedObjects.Count != 0)
                {
                    lstDriverStoreEntries.UncheckAll();
                }
                else
                {
                    lstDriverStoreEntries.CheckAll();
                }
            }
        }

        private void ctxMenuSelect_Click(object sender, EventArgs e)
        {
            if ((lstDriverStoreEntries.Objects != null))
            {
                ArrayList list = new ArrayList();
                if (lstDriverStoreEntries.CheckedObjects != null && lstDriverStoreEntries.CheckedObjects.Count > 0)
                {
                    list.AddRange(lstDriverStoreEntries.CheckedObjects);
                }

                if (lstDriverStoreEntries.SelectedObjects != null && lstDriverStoreEntries.SelectedObjects.Count > 0)
                {
                    if (new ArrayList(lstDriverStoreEntries.SelectedObjects).ToArray().All(i => lstDriverStoreEntries.CheckedObjects.Contains(i)))
                    {
                        foreach (var item in lstDriverStoreEntries.SelectedObjects)
                        {
                            list.Remove(item);
                        }
                    }
                    else
                    {
                        list.AddRange(lstDriverStoreEntries.SelectedObjects);
                    }
                }

                lstDriverStoreEntries.CheckedObjects = list;
            }
        }

        private void ctxMenuDelete_Click(object sender, EventArgs e)
        {
            if (lstDriverStoreEntries.SelectedObjects != null)
            {
                List<DriverStoreEntry> driverStoreEntries = new List<DriverStoreEntry>();

                foreach (DriverStoreEntry item in lstDriverStoreEntries.SelectedObjects)
                {
                    driverStoreEntries.Add(item);
                }

                DeleteDriverStoreEntries(driverStoreEntries);
            }
        }

        private void buttonRunAsAdmin_Click(object sender, EventArgs e)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo();
            processInfo.Verb = "runas";
            processInfo.FileName = Assembly.GetExecutingAssembly().Location;

            try
            {
                Process.Start(processInfo);
            }
            catch (Win32Exception ex)
            {
                // Ignore error 1223: The operation was canceled by the user.
                if (ex.NativeErrorCode == 1223)
                {
                    return;
                }

                throw;
            }

            Application.Exit();
        }

        private void ctxMenuSelectOldDrivers_Click(object sender, EventArgs e)
        {
            if (lstDriverStoreEntries.Objects != null)
            {
                List<DriverStoreEntry> driverStoreEntryList = lstDriverStoreEntries.Objects as List<DriverStoreEntry>;

                lstDriverStoreEntries.CheckedObjects = driverStoreEntryList
                    .GroupBy(entry => entry.DriverInfName)
                    .SelectMany(g => g.OrderByDescending(row => row.DriverDate).Skip(1))
                    .ToArray();
            }
        }

        private void buttonSelectOldDrivers_Click(object sender, EventArgs e)
        {
            ctxMenuSelectOldDrivers_Click(sender, e);
        }

        private void toolStripViewLogsButton_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(TextFileTraceListener.LastTraceFile))
            {
                Process.Start(TextFileTraceListener.LastTraceFile);
            }
            else
            {
                MessageBox.Show("The log file cannot be found.", "Error");
            }
        }

        private void ctxMenuExport_Click(object sender, EventArgs e)
        {
            // Check if there are any entries
            if (lstDriverStoreEntries.Objects != null)
            {
                try
                {
                    List<DriverStoreEntry> ldse = lstDriverStoreEntries.Objects as List<DriverStoreEntry>;
                    IExport exporter = new CSVExporter();   // TODO: Factory?? Change this when we add support for 
                                                            // direct Excel export
                    string fileName = exporter.Export(ldse);

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string message = $"Contents saved to {fileName}. Export Completed.";
                        MessageBox.Show(message);
                        ShowStatus(message, Status.Normal);
                    }
                }
                catch (Exception ex)
                {
                    string message = $"Export failed: {ex.ToString()}";
                    MessageBox.Show(message);
                    ShowStatus(message, Status.Error);
                }
            }
        }

        private void lstDriverStoreEntries_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            IList checkedObjects = lstDriverStoreEntries.CheckedObjects;

            if (checkedObjects != null && checkedObjects.Count > 0)
            {
                long totalSize = 0;

                foreach (DriverStoreEntry item in checkedObjects)
                {
                    totalSize += item.DriverSize;
                }

                ShowStatus($"Selected {checkedObjects.Count} Driver(s). Total size: {DriverStoreEntry.GetBytesReadable(totalSize)}.", Status.Normal);
            }
            else
            {
                ShowStatus($"Selected 0 Driver.", Status.Normal);
            }
        }
    }
}
