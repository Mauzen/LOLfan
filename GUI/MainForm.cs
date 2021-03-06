/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2013 Michael Möller <mmoeller@openhardwaremonitor.org>
	Copyright (C) 2010 Paul Werelds <paul@werelds.net>
	Copyright (C) 2012 Prince Samuel <prince.samuel@gmail.com>
    Copyright (C) 2017 Michel Soll <msoll@web.de>

*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Aga.Controls.Tree;
using Aga.Controls.Tree.NodeControls;
using LOLFan.Hardware;
using LOLFan.WMI;
using LOLFan.Utilities;
using System.Diagnostics;
using System.Threading;
using System.Globalization;
using LOLFan.Hardware.Virtual;

namespace LOLFan.GUI {
  public partial class MainForm : Form {
    static readonly int MAX_VIRTUAL_CONTAINERS = 32;

    private PersistentSettings settings;
    private UnitManager unitManager;
    private Computer computer;
    private Node root;
    private TreeModel treeModel;
    private IDictionary<ISensor, Color> sensorPlotColors = 
      new Dictionary<ISensor, Color>();
        private List<SensorNode> overviewSensors = new List<SensorNode>();
        private List<Label> overviewLabels = new List<Label>();
        private Color[] plotColorPalette;
    private SystemTray systemTray;    
    private StartupManager startupManager = new StartupManager();
    private UpdateVisitor updateVisitor = new UpdateVisitor();
    //private SensorGadget gadget;
    private Form plotForm;
    private PlotPanel plotPanel;

    private UserOption showHiddenSensors;
    private UserOption showPlot;
    private UserOption showValue;
    private UserOption showMin;
    private UserOption showMax;
    private UserOption startMinimized;
    private UserOption minimizeToTray;
    private UserOption minimizeOnClose;
    private UserOption autoStart;

    private UserOption readMainboardSensors;
    private UserOption readCpuSensors;
    private UserOption readRamSensors;
    private UserOption readGpuSensors;
    private UserOption readFanControllersSensors;
    private UserOption readHddSensors;

    //private UserOption showGadget;
    private UserRadioGroup plotLocation;
    private WmiProvider wmiProvider;

    private UserOption runWebServer;
    private HttpServer server;

    private UserOption logSensors;
    private UserRadioGroup loggingInterval;
    private Logger logger;

    private List<ISensor> sensors;
    private bool hwLoaded = false;
    private bool firstStart = false;

    private DisplayConnector lcd;

        private FanControllerManager fanControllerManager;

    private bool selectionDragging = false;

        private HotkeyManager hotkeyManager;

    public MainForm() {      
      InitializeComponent();

      // check if the LOLFanrLib assembly has the correct version
      if (Assembly.GetAssembly(typeof(Computer)).GetName().Version !=
        Assembly.GetExecutingAssembly().GetName().Version) {
        MessageBox.Show(
          "The version of the file LOLFanLib.dll is incompatible.",
          "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Environment.Exit(0);
      }

            //Thread.CurrentThread.CurrentCulture = new CultureInfo("en-Us");

            this.settings = new PersistentSettings();      
      this.settings.Load(Path.ChangeExtension(
        Application.ExecutablePath, ".config"));

            Program.Settings = settings;

            if (!File.Exists(Path.ChangeExtension(Application.ExecutablePath, ".config")))
            {
                firstStart = true;
            }

      this.unitManager = new UnitManager(settings);

      // make sure the buffers used for double buffering are not disposed 
      // after each draw call
      BufferedGraphicsManager.Current.MaximumBuffer =
        Screen.PrimaryScreen.Bounds.Size;  

      // set the DockStyle here, to avoid conflicts with the MainMenu
      this.splitContainer.Dock = DockStyle.Fill;
            
      this.Font = SystemFonts.MessageBoxFont;
      treeView.Font = SystemFonts.MessageBoxFont;

      plotPanel = new PlotPanel(settings, unitManager);
      plotPanel.Font = SystemFonts.MessageBoxFont;
      plotPanel.Dock = DockStyle.Fill;
      
      nodeCheckBox.IsVisibleValueNeeded += nodeCheckBox_IsVisibleValueNeeded;
      nodeTextBoxText.DrawText += nodeTextBoxText_DrawText;
      nodeTextBoxValue.DrawText += nodeTextBoxText_DrawText;
      nodeTextBoxMin.DrawText += nodeTextBoxText_DrawText;
      nodeTextBoxMax.DrawText += nodeTextBoxText_DrawText;
      nodeTextBoxText.EditorShowing += nodeTextBoxText_EditorShowing;

      foreach (TreeColumn column in treeView.Columns) 
        column.Width = Math.Max(20, Math.Min(400,
          settings.GetValue("treeView.Columns." + column.Header + ".Width",
          column.Width)));

      treeModel = new TreeModel();
      root = new Node(System.Environment.MachineName, new Identifier(System.Environment.MachineName), settings);
      root.Image = Utilities.EmbeddedResources.GetImage("computer.png");
      
      treeModel.Nodes.Add(root);
      treeView.Model = treeModel;

      this.computer = new Computer(settings);

      systemTray = new SystemTray(computer, settings, unitManager);
      systemTray.HideShowCommand += hideShowClick;
      systemTray.ExitCommand += exitClick;
      systemTray.DisplayToggleCommand += displayToggleClick;

            int p = (int)Environment.OSVersion.Platform;
      if ((p == 4) || (p == 128)) { // Unix
        treeView.RowHeight = Math.Max(treeView.RowHeight, 18); 
        splitContainer.BorderStyle = BorderStyle.None;
        splitContainer.Border3DStyle = Border3DStyle.Adjust;
        splitContainer.SplitterWidth = 4;
        treeView.BorderStyle = BorderStyle.Fixed3D;
        plotPanel.BorderStyle = BorderStyle.Fixed3D;
        //gadgetMenuItem.Visible = false;
        minCloseMenuItem.Visible = false;
        minTrayMenuItem.Visible = false;
        startMinMenuItem.Visible = false;
      } else { // Windows
        treeView.RowHeight = Math.Max(treeView.Font.Height + 1, 18); 

        //gadget = new SensorGadget(computer, settings, unitManager);
        //gadget.HideShowCommand += hideShowClick;

        wmiProvider = new WmiProvider(computer);
      }

      logger = new Logger(computer);

      plotColorPalette = new Color[13];
      plotColorPalette[0] = Color.Blue;
      plotColorPalette[1] = Color.OrangeRed;
      plotColorPalette[2] = Color.Green;
      plotColorPalette[3] = Color.LightSeaGreen;
      plotColorPalette[4] = Color.Goldenrod;
      plotColorPalette[5] = Color.DarkViolet;
      plotColorPalette[6] = Color.YellowGreen;
      plotColorPalette[7] = Color.SaddleBrown;
      plotColorPalette[8] = Color.RoyalBlue;
      plotColorPalette[9] = Color.DeepPink;
      plotColorPalette[10] = Color.MediumSeaGreen;
      plotColorPalette[11] = Color.Olive;
      plotColorPalette[12] = Color.Firebrick;
      
      computer.HardwareAdded += new HardwareEventHandler(HardwareAdded);
      computer.HardwareRemoved += new HardwareEventHandler(HardwareRemoved);

      SharedData.AllSensors = new SensorList();

      computer.Open();

      LoadVirtualSensorContainers();            
            
            

      overview.Settings = settings;

      timer.Enabled = true;

      showHiddenSensors = new UserOption("hiddenMenuItem", false,
        hiddenMenuItem, settings);
      showHiddenSensors.Changed += delegate(object sender, EventArgs e) {
        treeModel.ForceVisible = showHiddenSensors.Value;
      };

      showValue = new UserOption("valueMenuItem", true, valueMenuItem,
        settings);
      showValue.Changed += delegate(object sender, EventArgs e) {
        treeView.Columns[1].IsVisible = showValue.Value;
      };

      showMin = new UserOption("minMenuItem", false, minMenuItem, settings);
      showMin.Changed += delegate(object sender, EventArgs e) {
        treeView.Columns[2].IsVisible = showMin.Value;
      };

      showMax = new UserOption("maxMenuItem", true, maxMenuItem, settings);
      showMax.Changed += delegate(object sender, EventArgs e) {
        treeView.Columns[3].IsVisible = showMax.Value;
      };

      startMinimized = new UserOption("startMinMenuItem", false,
        startMinMenuItem, settings);

      minimizeToTray = new UserOption("minTrayMenuItem", true,
        minTrayMenuItem, settings);
      minimizeToTray.Changed += delegate(object sender, EventArgs e) {
        systemTray.IsMainIconEnabled = minimizeToTray.Value;
      };

      minimizeOnClose = new UserOption("minCloseMenuItem", false,
        minCloseMenuItem, settings);

      autoStart = new UserOption(null, startupManager.Startup,
        startupMenuItem, settings);
      autoStart.Changed += delegate(object sender, EventArgs e) {
        try {
          startupManager.Startup = autoStart.Value;
        } catch (InvalidOperationException) {
          MessageBox.Show("Updating the auto-startup option failed.", "Error", 
            MessageBoxButtons.OK, MessageBoxIcon.Error);
          autoStart.Value = startupManager.Startup;
        }
      };

      readMainboardSensors = new UserOption("mainboardMenuItem", true, 
        mainboardMenuItem, settings);
      readMainboardSensors.Changed += delegate(object sender, EventArgs e) {
        computer.MainboardEnabled = readMainboardSensors.Value;
      };

      readCpuSensors = new UserOption("cpuMenuItem", true,
        cpuMenuItem, settings);
      readCpuSensors.Changed += delegate(object sender, EventArgs e) {
        computer.CPUEnabled = readCpuSensors.Value;
      };

      readRamSensors = new UserOption("ramMenuItem", true,
        ramMenuItem, settings);
      readRamSensors.Changed += delegate(object sender, EventArgs e) {
        computer.RAMEnabled = readRamSensors.Value;
      };

      readGpuSensors = new UserOption("gpuMenuItem", true,
        gpuMenuItem, settings);
      readGpuSensors.Changed += delegate(object sender, EventArgs e) {
        computer.GPUEnabled = readGpuSensors.Value;
      };

      readFanControllersSensors = new UserOption("fanControllerMenuItem", true,
        fanControllerMenuItem, settings);
      readFanControllersSensors.Changed += delegate(object sender, EventArgs e) {
        computer.FanControllerEnabled = readFanControllersSensors.Value;
      };

      readHddSensors = new UserOption("hddMenuItem", true, hddMenuItem,
        settings);
      readHddSensors.Changed += delegate(object sender, EventArgs e) {
        computer.HDDEnabled = readHddSensors.Value;
      };

      /*showGadget = new UserOption("gadgetMenuItem", false, gadgetMenuItem,
        settings);
      showGadget.Changed += delegate(object sender, EventArgs e) {
        if (gadget != null) 
          gadget.Visible = showGadget.Value;
      };*/

      celsiusMenuItem.Checked = 
        unitManager.TemperatureUnit == TemperatureUnit.Celsius;
      fahrenheitMenuItem.Checked = !celsiusMenuItem.Checked;

      server = new HttpServer(root, this.settings.GetValue("listenerPort", 8085));
      if (server.PlatformNotSupported) {
        webMenuItemSeparator.Visible = false;
        webMenuItem.Visible = false;
      }

      runWebServer = new UserOption("runWebServerMenuItem", false,
        runWebServerMenuItem, settings);
      runWebServer.Changed += delegate(object sender, EventArgs e) {
        if (runWebServer.Value)
          server.StartHTTPListener();
        else
          server.StopHTTPListener();
      };

      logSensors = new UserOption("logSensorsMenuItem", false, logSensorsMenuItem,
        settings);

      loggingInterval = new UserRadioGroup("loggingInterval", 0,
        new[] { log1sMenuItem, log2sMenuItem, log5sMenuItem, log10sMenuItem,
        log30sMenuItem, log1minMenuItem, log2minMenuItem, log5minMenuItem, 
        log10minMenuItem, log30minMenuItem, log1hMenuItem, log2hMenuItem, 
        log6hMenuItem},
        settings);
      loggingInterval.Changed += (sender, e) => {
        switch (loggingInterval.Value) {
          case 0: logger.LoggingInterval = new TimeSpan(0, 0, 1); break;
          case 1: logger.LoggingInterval = new TimeSpan(0, 0, 2); break;
          case 2: logger.LoggingInterval = new TimeSpan(0, 0, 5); break;
          case 3: logger.LoggingInterval = new TimeSpan(0, 0, 10); break;
          case 4: logger.LoggingInterval = new TimeSpan(0, 0, 30); break;
          case 5: logger.LoggingInterval = new TimeSpan(0, 1, 0); break;
          case 6: logger.LoggingInterval = new TimeSpan(0, 2, 0); break;
          case 7: logger.LoggingInterval = new TimeSpan(0, 5, 0); break;
          case 8: logger.LoggingInterval = new TimeSpan(0, 10, 0); break;
          case 9: logger.LoggingInterval = new TimeSpan(0, 30, 0); break;
          case 10: logger.LoggingInterval = new TimeSpan(1, 0, 0); break;
          case 11: logger.LoggingInterval = new TimeSpan(2, 0, 0); break;
          case 12: logger.LoggingInterval = new TimeSpan(6, 0, 0); break;
        }
      };

      InitializePlotForm();

                                   

      startupMenuItem.Visible = startupManager.IsAvailable;
      
      if (startMinMenuItem.Checked) {
        if (!minTrayMenuItem.Checked) {
          WindowState = FormWindowState.Minimized;
          Show();
        }
      } else {
        Show();
      }

      // Create a handle, otherwise calling Close() does not fire FormClosed     
      IntPtr handle = Handle;

      // Make sure the settings are saved when the user logs off
      Microsoft.Win32.SystemEvents.SessionEnded += delegate {
        treeView.Collapsed -= treeView_CollapsedExpanded;
        treeView.Expanded -= treeView_CollapsedExpanded;
        computer.Close();
        SaveConfiguration();
        if (runWebServer.Value) 
          server.Quit();
      };

            groupItemsListBox.ItemCheck += GroupItemsListBox_ItemCheck;


            hotkeyManager = new HotkeyManager(this, settings);
            settings.hotkeyManager = hotkeyManager; 
            
            if (settings.GetValue("CheckForUpdates", false))
            {
                new UpdateChecker().Check(false);
            }  
            
                     
            
    }

        
        private void LoadVirtualSensorContainers()
        {
            for (int i = 0; i < MAX_VIRTUAL_CONTAINERS; i++)
            {
                if (settings.Contains(new Identifier("virtual", i+"", "sensorindex").ToString())) {
                    VirtualSensorContainer cont = new VirtualSensorContainer("Virtual Sensor Container " + i, new Identifier("virtual", i+""), settings);
                    int sensorIdx;
                    int.TryParse(settings.GetValue(new Identifier("virtual", i + "", "sensorindex").ToString(), "0"), out sensorIdx);
                    // Load sensors
                    for (int si = 0; si < sensorIdx; si++)
                    {
                        if (settings.Contains(new Identifier("virtual", i + "", si+"", "sensortype").ToString()))
                        {
                            int type = 4;
                            int.TryParse(settings.GetValue(new Identifier("virtual", i + "", si + "", "sensortype").ToString(), "4"), out type);
                            VirtualSensor s = new VirtualSensor("Virtual Sensor " + si, si, (SensorType)type, cont, settings);
                            cont.AddVirtualSensor(s);
                        }
                    }
                    computer.AddVirtualSensorContainer(cont);
                }
            }
            
        }

        private void GroupItemsListBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            switch (e.CurrentValue)
            {
                case CheckState.Checked:
                    e.NewValue = CheckState.Indeterminate;
                    break;

                case CheckState.Indeterminate:
                    e.NewValue = CheckState.Unchecked;
                    break;

                case CheckState.Unchecked:
                    e.NewValue = CheckState.Checked;
                    break;
            }
        }

        private List<ISensor> fetchSensors(Computer c)
        {
            List<ISensor> sensors = new List<ISensor>();
            foreach (IHardware h in c.Hardware)
            {
                sensors.AddRange(h.Sensors);
                sensors.AddRange(fetchSubSensors(h));

            }

            return sensors;
        }

        private List<ISensor> fetchSubSensors(IHardware hardware)
        {
            List<ISensor> sensors = new List<ISensor>();
            foreach (IHardware h in hardware.SubHardware)
            {
                sensors.AddRange(h.Sensors);
            }

            return sensors;
        }

    private void InitializePlotForm() {
      plotForm = new Form();
      plotForm.FormBorderStyle = FormBorderStyle.SizableToolWindow;
      plotForm.ShowInTaskbar = false;
      plotForm.StartPosition = FormStartPosition.Manual;
      this.AddOwnedForm(plotForm);
      plotForm.Bounds = new Rectangle {
        X = settings.GetValue("plotForm.Location.X", -100000),
        Y = settings.GetValue("plotForm.Location.Y", 100),
        Width = settings.GetValue("plotForm.Width", 600),
        Height = settings.GetValue("plotForm.Height", 400)
      };

      showPlot = new UserOption("plotMenuItem", false, plotMenuItem, settings);
      plotLocation = new UserRadioGroup("plotLocation", 0,
        new[] { plotWindowMenuItem, plotBottomMenuItem, plotRightMenuItem },
        settings);

      showPlot.Changed += delegate(object sender, EventArgs e) {
        if (plotLocation.Value == 0) {
          if (showPlot.Value && this.Visible)
            plotForm.Show();
          else
            plotForm.Hide();
        } else {
          splitContainer.Panel2Collapsed = !showPlot.Value;
        }
        treeView.Invalidate();
      };
      plotLocation.Changed += delegate(object sender, EventArgs e) {
        switch (plotLocation.Value) {
          case 0:
            splitContainer.Panel2.Controls.Clear();
            splitContainer.Panel2Collapsed = true;
            plotForm.Controls.Add(plotPanel);
            if (showPlot.Value && this.Visible)
              plotForm.Show();
            break;
          case 1:
            plotForm.Controls.Clear();
            plotForm.Hide();
            splitContainer.Orientation = Orientation.Horizontal;
            splitContainer.Panel2.Controls.Add(plotPanel);
            splitContainer.Panel2Collapsed = !showPlot.Value;
            break;
          case 2:
            plotForm.Controls.Clear();
            plotForm.Hide();
            splitContainer.Orientation = Orientation.Vertical;
            splitContainer.Panel2.Controls.Add(plotPanel);
            splitContainer.Panel2Collapsed = !showPlot.Value;
            break;
        }
      };

      plotForm.FormClosing += delegate(object sender, FormClosingEventArgs e) {
        if (e.CloseReason == CloseReason.UserClosing) {
          // just switch off the plotting when the user closes the form
          if (plotLocation.Value == 0) {
            showPlot.Value = false;
          }
          e.Cancel = true;
        }
      };

      EventHandler moveOrResizePlotForm = delegate(object sender, EventArgs e) {
        if (plotForm.WindowState != FormWindowState.Minimized) {
          settings.SetValue("plotForm.Location.X", plotForm.Bounds.X);
          settings.SetValue("plotForm.Location.Y", plotForm.Bounds.Y);
          settings.SetValue("plotForm.Width", plotForm.Bounds.Width);
          settings.SetValue("plotForm.Height", plotForm.Bounds.Height);
        }
      };
      plotForm.Move += moveOrResizePlotForm;
      plotForm.Resize += moveOrResizePlotForm;

      plotForm.VisibleChanged += delegate(object sender, EventArgs e) {
        Rectangle bounds = new Rectangle(plotForm.Location, plotForm.Size);
        Screen screen = Screen.FromRectangle(bounds);
        Rectangle intersection =
          Rectangle.Intersect(screen.WorkingArea, bounds);
        if (intersection.Width < Math.Min(16, bounds.Width) ||
            intersection.Height < Math.Min(16, bounds.Height)) {
          plotForm.Location = new Point(
            screen.WorkingArea.Width / 2 - bounds.Width / 2,
            screen.WorkingArea.Height / 2 - bounds.Height / 2);
        }
      };

      this.VisibleChanged += delegate(object sender, EventArgs e) {
        if (this.Visible && showPlot.Value && plotLocation.Value == 0)
          plotForm.Show();
        else
          plotForm.Hide();
      };
    }

    private void InsertSorted(Collection<Node> nodes, HardwareNode node) {
      int i = 0;
      while (i < nodes.Count && nodes[i] is HardwareNode &&
        ((HardwareNode)nodes[i]).Hardware.HardwareType < 
          node.Hardware.HardwareType)
        i++;
      nodes.Insert(i, node);
    }
    
    private void SubHardwareAdded(IHardware hardware, Node node) {
      HardwareNode hardwareNode = 
        new HardwareNode(hardware, settings, unitManager);
      hardwareNode.PlotSelectionChanged += PlotSelectionChanged;
      hardwareNode.OverviewSelectionChanged += OverviewSelectionChanged;

            InsertSorted(node.Nodes, hardwareNode);

      foreach (IHardware subHardware in hardware.SubHardware)
        SubHardwareAdded(subHardware, hardwareNode);  
    }

    private void HardwareAdded(IHardware hardware) {
      SubHardwareAdded(hardware, root);
      PlotSelectionChanged(this, null);
    }

    private void HardwareRemoved(IHardware hardware) {
      List<HardwareNode> nodesToRemove = new List<HardwareNode>();
      foreach (Node node in root.Nodes) {
        HardwareNode hardwareNode = node as HardwareNode;
        if (hardwareNode != null && hardwareNode.Hardware == hardware)
          nodesToRemove.Add(hardwareNode);
      }
      foreach (HardwareNode hardwareNode in nodesToRemove) {
        root.Nodes.Remove(hardwareNode);
        hardwareNode.PlotSelectionChanged -= PlotSelectionChanged;
      }
      PlotSelectionChanged(this, null);
    }

    private void nodeTextBoxText_DrawText(object sender, DrawEventArgs e) {       
      Node node = e.Node.Tag as Node;
      if (node != null) {
        Color color;
        if (node.IsVisible) {
          SensorNode sensorNode = node as SensorNode;
          if (plotMenuItem.Checked && sensorNode != null &&
            sensorPlotColors.TryGetValue(sensorNode.Sensor, out color))
            e.TextColor = color;
        } else {
          e.TextColor = Color.DarkGray;
        }
      }
    }

    private void PlotSelectionChanged(object sender, EventArgs e) {
      List<ISensor> selected = new List<ISensor>();
      IDictionary<ISensor, Color> colors = new Dictionary<ISensor, Color>();
      int colorIndex = 0;
      foreach (TreeNodeAdv node in treeView.AllNodes) {
        SensorNode sensorNode = node.Tag as SensorNode;
        if (sensorNode != null) {
          if (sensorNode.Plot) {
            if (!sensorNode.PenColor.HasValue) {
              colors.Add(sensorNode.Sensor,
                plotColorPalette[colorIndex % plotColorPalette.Length]);
            }
            selected.Add(sensorNode.Sensor);
          }
          colorIndex++;
        }
      }

      // if a sensor is assigned a color that's already being used by another 
      // sensor, try to assign it a new color. This is done only after the 
      // previous loop sets an unchanging default color for all sensors, so that 
      // colors jump around as little as possible as sensors get added/removed 
      // from the plot
      var usedColors = new List<Color>();
      foreach (var curSelectedSensor in selected) {
        if (!colors.ContainsKey(curSelectedSensor)) continue;
        var curColor = colors[curSelectedSensor];
        if (usedColors.Contains(curColor)) {
          foreach (var potentialNewColor in plotColorPalette) {
            if (!colors.Values.Contains(potentialNewColor)) {
              colors[curSelectedSensor] = potentialNewColor;
              usedColors.Add(potentialNewColor);
              break;
            }
          }
        } else {
          usedColors.Add(curColor);
        }
      }

      foreach (TreeNodeAdv node in treeView.AllNodes) {
        SensorNode sensorNode = node.Tag as SensorNode;
        if (sensorNode != null && sensorNode.Plot && sensorNode.PenColor.HasValue)
          colors.Add(sensorNode.Sensor, sensorNode.PenColor.Value);
      }

      sensorPlotColors = colors;
      plotPanel.SetSensors(selected, colors);
    }

        // Changes to the sensors selected to be shown on overview
        private void OverviewSelectionChanged(object sender, EventArgs e)
        {
            List<SensorNode> selected = new List<SensorNode>();
            foreach (TreeNodeAdv node in treeView.AllNodes)
            {
                SensorNode sensorNode = node.Tag as SensorNode;
                if (sensorNode != null)
                {
                    if (sensorNode.Overview)
                    {
                        selected.Add(sensorNode);
                    }
                }
            }
            

            SetOverviewSensors(selected);
        }

        private void SetOverviewSensors(List<SensorNode> selected)
        {
            overviewSensors = selected;

            overview.Clear();
            foreach (SensorNode node in overviewSensors)
            {
                overview.AddSensorNode(node);                
            }

        }


        private void nodeTextBoxText_EditorShowing(object sender,
      CancelEventArgs e) 
    {
      e.Cancel = !(treeView.CurrentNode != null &&
        (treeView.CurrentNode.Tag is SensorNode || 
         treeView.CurrentNode.Tag is HardwareNode));
    }

    private void nodeCheckBox_IsVisibleValueNeeded(object sender, 
      NodeControlValueEventArgs e) {
      SensorNode node = e.Node.Tag as SensorNode;
      e.Value = (node != null) && plotMenuItem.Checked;
    }

    private void exitClick(object sender, EventArgs e) {
      Close();
    }

        private void displayToggleClick(object sender, EventArgs e)
        {
            lcd.ToggleDisplay();
        }



        private void HardwareLoaded()
        {
            // Delayed init stuff that requires sensor data
            sensors = new List<ISensor>();
            sensors.AddRange(fetchSensors(computer));
            
            SharedData.AllSensors.AddRange(sensors);


            lcd = new DisplayConnector(computer.Hardware, sensors, settings);
            lcdFormatText.Text = lcd.FormatString;
            if (lcd.ConnectionMode == DisplayConnector.LCDConnectionMode.NONE) lcdFormatText.Enabled = false;


            // Fan controller tab initialisation
            fanControllerManager = new FanControllerManager(sensors, settings);


            // Fan controller list prep
            foreach (FanController c in fanControllerManager.Controllers)
            {
                controllerListBox.AddItem(c);
            }   

            controllerListBox.ItemCheck += delegate (Object sender, ItemCheckEventArgs e)
            {
                fanControllerManager.Controllers[e.Index].Enabled = (e.NewValue == CheckState.Checked);
            };

            // Force overview nodes update
            OverviewSelectionChanged(this, null);

            // Apply Monitor collapse
            foreach (TreeNodeAdv node in treeView.AllNodes) {
                node.IsExpanded = !(node.Tag as Node).Collapsed;
            }

            treeView.Collapsed += treeView_CollapsedExpanded;
            treeView.Expanded += treeView_CollapsedExpanded;

            hwLoaded = true;

            if (firstStart)
            {
                AutoSetupOverview();
                AutoSetupNames();
                if (MessageBox.Show("Would you like to submit a hardware report to help fix possible bugs?" + Environment.NewLine + "This helps spotting the problem if you encounter a crash.", "Submit report", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                    ReportForm form = new ReportForm();
                    form.Report = computer.GetReport();
                    form.ShowDialog();
                }

            }

            new HintsForm(HintsForm.Hints.GettingStarted);
        }

        private void treeView_CollapsedExpanded(object sender, TreeViewAdvEventArgs e)
        {
            Node node = e.Node.Tag as Node;
            node.Collapsed = !e.Node.IsExpanded;
        }

        private void AutoSetupOverview()
        {
            foreach (TreeNodeAdv node in treeView.AllNodes)
            {
                SensorNode sensorNode = node.Tag as SensorNode;
                if (sensorNode != null)
                {
                    if (sensorNode.Sensor.SensorType == SensorType.Fan || sensorNode.Sensor.SensorType == SensorType.Temperature)
                    {
                        sensorNode.Overview = true;
                    }
                }
            }
        }

        private void AutoSetupNames()
        {
            // HDD temp names
            int idx = 0;
            foreach (ISensor sensor in sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Hardware.HardwareType == HardwareType.HDD)
                {
                    sensor.Name = "HDD" + idx;
                    idx++;
                }
            }
        }

        public event EventHandler SensorUpdate;

        private int delayCount = 0;
        private void timer_Tick(object sender, EventArgs e)
        {
            computer.Accept(updateVisitor);
            treeView.Invalidate();
            plotPanel.InvalidatePlot();
            
            if (!hwLoaded && computer.Hardware.Length > 0) HardwareLoaded();
            else
            {
                lcd.Update();
                if (SensorUpdate != null) SensorUpdate.Invoke(this, null);
            }

            fanControllerManager.Update();
          if (this.Visible) overview.UpdateItems();// UpdateOverviewSensors();
          systemTray.Redraw();
          //if (gadget != null)
          //  gadget.Redraw();

          if (wmiProvider != null)
            wmiProvider.Update();


          if (logSensors != null && logSensors.Value && delayCount >= 4)
            logger.Log();

          if (delayCount < 4)
            delayCount++;
        }

    private void SaveConfiguration() {
      plotPanel.SetCurrentSettings();

            // Save control calibration
            foreach (Hardware.Sensor s in sensors)
            {
                if (s.Control != null)
                {
                    if (s.Control.Calibrated != null)
                    {
                        s.Control.Calibrated.SaveValuesToSettings();
                    }
                }
            }

      foreach (TreeColumn column in treeView.Columns)
        settings.SetValue("treeView.Columns." + column.Header + ".Width",
          column.Width);

      this.settings.SetValue("listenerPort", server.ListenerPort);


            fanControllerManager.SaveToSettings();

      string fileName = Path.ChangeExtension(
          System.Windows.Forms.Application.ExecutablePath, ".config");
      try {
        settings.Save(fileName);
      } catch (UnauthorizedAccessException) {
        MessageBox.Show("Access to the path '" + fileName + "' is denied. " +
          "The current settings could not be saved.",
          "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
      } catch (IOException) {
        MessageBox.Show("The path '" + fileName + "' is not writeable. " +
          "The current settings could not be saved.",
          "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private void MainForm_Load(object sender, EventArgs e) {
      Rectangle newBounds = new Rectangle {
        X = settings.GetValue("mainForm.Location.X", Location.X),
        Y = settings.GetValue("mainForm.Location.Y", Location.Y),
        Width = settings.GetValue("mainForm.Width", 470),
        Height = settings.GetValue("mainForm.Height", 470)
      };

      Rectangle fullWorkingArea = new Rectangle(int.MaxValue, int.MaxValue,
        int.MinValue, int.MinValue);

      foreach (Screen screen in Screen.AllScreens)
        fullWorkingArea = Rectangle.Union(fullWorkingArea, screen.Bounds);

      Rectangle intersection = Rectangle.Intersect(fullWorkingArea, newBounds);
      if (intersection.Width < 20 || intersection.Height < 20 ||
        !settings.Contains("mainForm.Location.X")
      ) {
        newBounds.X = (Screen.PrimaryScreen.WorkingArea.Width / 2) -
                      (newBounds.Width/2);

        newBounds.Y = (Screen.PrimaryScreen.WorkingArea.Height / 2) -
                      (newBounds.Height / 2);
      }

      numericUpDown1.Value = settings.GetValue("refresh_rate", 1000);
      SharedData.UpdateRate = (int)numericUpDown1.Value;

      this.Bounds = newBounds;
    }
    
    private void MainForm_FormClosed(object sender, FormClosedEventArgs e) {
      Visible = false;
      treeView.Collapsed -= treeView_CollapsedExpanded;
      treeView.Expanded -= treeView_CollapsedExpanded;
      systemTray.IsMainIconEnabled = false;
      timer.Enabled = false;            
      computer.Close();
      SaveConfiguration();
      if (runWebServer.Value)
          server.Quit();
      systemTray.Dispose();

            if (lcd != null) lcd.SetDisplay(false);
    }

    private void aboutMenuItem_Click(object sender, EventArgs e) {
      new AboutBox().ShowDialog();
    }

    private void treeView_Click(object sender, EventArgs e) {

      MouseEventArgs m = e as MouseEventArgs;
      if (m == null || m.Button != MouseButtons.Right)
        return;

      NodeControlInfo info = treeView.GetNodeControlInfoAt(
        new Point(m.X, m.Y)
      );
      treeView.SelectedNode = info.Node;
      if (info.Node != null) {
        SensorNode node = info.Node.Tag as SensorNode;


        if (node != null && node.Sensor != null) {
          treeContextMenu.MenuItems.Clear();
            MenuItem item = new MenuItem(node.Sensor.Identifier.ToString() + "   [copy]");
            treeContextMenu.MenuItems.Add(item);
                    item.Click += delegate (object obj, EventArgs args)
                    {
                        Clipboard.SetText("{" + node.Sensor.Identifier.ToString() + "}");
                    };
            treeContextMenu.MenuItems.Add(new MenuItem("-"));

            if (node.Sensor.Parameters.Length > 0) {
            

            item = new MenuItem("Parameters...");
            item.Click += delegate(object obj, EventArgs args) {
              ShowParameterForm(node.Sensor);
            };
            treeContextMenu.MenuItems.Add(item);
          }

                    item = new MenuItem("Reset Min/Max");
                    item.Click += delegate (object obj, EventArgs args) {
                        node.Sensor.ResetMax();
                        node.Sensor.ResetMin();
                    };
                    treeContextMenu.MenuItems.Add(item);

          if (nodeTextBoxText.EditEnabled) {
            item = new MenuItem("Rename");
            item.Click += delegate(object obj, EventArgs args) {
              nodeTextBoxText.BeginEdit();
            };
            treeContextMenu.MenuItems.Add(item);
          }
          if (node.IsVisible) {
            item = new MenuItem("Hide");
            item.Click += delegate(object obj, EventArgs args) {
              node.IsVisible = false;
            };
            treeContextMenu.MenuItems.Add(item);
          } else {
            item = new MenuItem("Unhide");
            item.Click += delegate(object obj, EventArgs args) {
              node.IsVisible = true;
            };
            treeContextMenu.MenuItems.Add(item);
          }
          treeContextMenu.MenuItems.Add(new MenuItem("-"));
          {
            item = new MenuItem("Pen Color...");
            item.Click += delegate(object obj, EventArgs args) {
              ColorDialog dialog = new ColorDialog();
              dialog.Color = node.PenColor.GetValueOrDefault();
              if (dialog.ShowDialog() == DialogResult.OK)
                node.PenColor = dialog.Color;
            };
            treeContextMenu.MenuItems.Add(item);
          }
          {
            item = new MenuItem("Reset Pen Color");
            item.Click += delegate(object obj, EventArgs args) {
              node.PenColor = null;
            };
            treeContextMenu.MenuItems.Add(item);
          }
          treeContextMenu.MenuItems.Add(new MenuItem("-"));
          {
            item = new MenuItem("Show in Tray");
            item.Checked = systemTray.Contains(node.Sensor);
            item.Click += delegate(object obj, EventArgs args) {
                if (systemTray.Contains(node.Sensor))
                systemTray.Remove(node.Sensor);
              else
                systemTray.Add(node.Sensor, true);
            };
            treeContextMenu.MenuItems.Add(item);
          }
                    /*if (gadget != null) {
                      MenuItem item = new MenuItem("Show in Gadget");
                      item.Checked = gadget.Contains(node.Sensor);
                      item.Click += delegate(object obj, EventArgs args) {
                        if (item.Checked) {
                          gadget.Remove(node.Sensor);
                        } else {
                          gadget.Add(node.Sensor);
                        }
                      };
                      treeContextMenu.MenuItems.Add(item);
                    }*/
                    //if (node.Sensor.SensorType == SensorType.Control || node.Sensor.SensorType == SensorType.Fan || node.Sensor.SensorType == SensorType.Temperature
                    //            || node.Sensor.SensorType == SensorType.Control)
                    //{
                {
                    item = new MenuItem("Show in Overview");
                    item.Checked = node.Overview;
                    item.Click += delegate (object obj, EventArgs args)
                    {
                        node.Overview = !node.Overview;
                    };
                    treeContextMenu.MenuItems.Add(item);
                }

            //}
          if (node.Sensor.Control != null) {
            treeContextMenu.MenuItems.Add(new MenuItem("-"));
            IControl control = node.Sensor.Control;
                        MenuItem settingsItem = new MenuItem("Settings...");
                        settingsItem.Click += delegate (object obj, EventArgs args)
                        {
                            ControlSettingsForm f = new ControlSettingsForm(node.Sensor.Name, control, this, settings);
                            f.Show();
                        };
                        treeContextMenu.MenuItems.Add(settingsItem);


            MenuItem controlItem = new MenuItem("Control");
            MenuItem defaultItem = new MenuItem("Default");
            defaultItem.Checked = control.ControlMode == ControlMode.Default;
            controlItem.MenuItems.Add(defaultItem);
            defaultItem.Click += delegate(object obj, EventArgs args) {
              control.SetDefault();
            };
            MenuItem manualItem = new MenuItem("Manual");
            controlItem.MenuItems.Add(manualItem);
            manualItem.Checked = control.ControlMode == ControlMode.Software;
            for (int i = 0; i <= 100; i += 5)
            {
                if (i <= control.MaxSoftwareValue &&
                    i >= control.MinSoftwareValue)
                {
                    item = new MenuItem(i + " %");
                    item.RadioCheck = true;
                    manualItem.MenuItems.Add(item);
                    item.Checked = control.ControlMode == ControlMode.Software &&
                        Math.Round(control.SoftwareValue) == i;
                    int softwareValue = i;
                    item.Click += delegate (object obj, EventArgs args)
                    {
                        control.SetSoftware(softwareValue);
                    };
                }
            }
                /*            control.
                for (int i = 0; i <= c; i += 5)
                {
                    if (i <= control.MaxSoftwareValue &&
                        i >= control.MinSoftwareValue)
                    {
                        MenuItem item = new MenuItem(i + " %");
                        item.RadioCheck = true;
                        manualItem.MenuItems.Add(item);
                        item.Checked = control.ControlMode == ControlMode.Software &&
                            Math.Round(control.SoftwareValue) == i;
                        int softwareValue = i;
                        item.Click += delegate (object obj, EventArgs args) {
                            control.SetSoftware(softwareValue);
                        };
                    }
                }*/
            treeContextMenu.MenuItems.Add(controlItem);
          }

                    if (node.Sensor.Hardware is VirtualSensorContainer)
                    {
                        treeContextMenu.MenuItems.Add(new MenuItem("-"));
                        MenuItem virtualSettingsItem = new MenuItem("Configure Virtual Sensor...");
                        virtualSettingsItem.Click += delegate (object obj, EventArgs args)
                        {
                            VirtualSensorEditForm f = new VirtualSensorEditForm(node.Sensor as VirtualSensor, settings);
                            f.Show();
                        };
                        treeContextMenu.MenuItems.Add(virtualSettingsItem);

                        MenuItem removeVirtualItem = new MenuItem("Delete Virtual Sensor");
                        removeVirtualItem.Click += delegate (object obj, EventArgs args)
                        {
                            VirtualSensor s = node.Sensor as VirtualSensor;
                            VirtualSensorContainer cont = s.Hardware as VirtualSensorContainer;
                            cont.RemoveVirtualSensor(s);
                            s.DeleteFromConfig();
                            
                        };
                        treeContextMenu.MenuItems.Add(removeVirtualItem);
                    }

                    treeContextMenu.Show(treeView, new Point(m.X, m.Y));
       }

        HardwareNode hardwareNode = info.Node.Tag as HardwareNode;
        if (hardwareNode != null && hardwareNode.Hardware != null) {
          treeContextMenu.MenuItems.Clear();
                    MenuItem item;
          if (nodeTextBoxText.EditEnabled) {
            item = new MenuItem("Rename");
            item.Click += delegate(object obj, EventArgs args) {
              nodeTextBoxText.BeginEdit();
            };
            treeContextMenu.MenuItems.Add(item);
          }
          if (hardwareNode.Hardware is VirtualSensorContainer)
                    {
                        item = new MenuItem("Add Virtual Sensor...");
                        item.Click += delegate (object obj, EventArgs args) {
                            VirtualSensorEditForm f = new VirtualSensorEditForm(null, settings);
                            f.ShowDialog();
                            if (f.DialogResult == DialogResult.OK)
                            {
                                VirtualSensorContainer c = hardwareNode.Hardware as VirtualSensorContainer;
                                int idx = c.GetNextSensorIndex();
                                VirtualSensor s = new VirtualSensor("Virtual Sensor " + idx, idx, f.SensorType, c, settings);
                                s.ValueStringInput = f.ValueStringInput;
                                c.AddVirtualSensor(s);
                            }
                        };
                        treeContextMenu.MenuItems.Add(item);
                    }
        if (hardwareNode.IsVisible)
        {
            item = new MenuItem("Hide");
            item.Click += delegate (object obj, EventArgs args) {
                hardwareNode.IsVisible = false;
            };
            treeContextMenu.MenuItems.Add(item);
        }
        else
        {
            item = new MenuItem("Unhide");
            item.Click += delegate (object obj, EventArgs args) {
                hardwareNode.IsVisible = true;
            };
            treeContextMenu.MenuItems.Add(item);
        }

                    treeContextMenu.Show(treeView, new Point(m.X, m.Y));
        }
      }
    }

    private void saveReportMenuItem_Click(object sender, EventArgs e) {
      string report = computer.GetReport();
      if (saveFileDialog.ShowDialog() == DialogResult.OK) {
        using (TextWriter w = new StreamWriter(saveFileDialog.FileName)) {
          w.Write(report);
        }
      }
    }

    private void SysTrayHideShow() {
      Visible = !Visible;
      if (Visible)
            {
                Activate();
                overview.UpdateItems();
            }
        
    }

        

    protected override void WndProc(ref Message m) {
      const int WM_SYSCOMMAND = 0x112;
      const int SC_MINIMIZE = 0xF020;
      const int SC_CLOSE = 0xF060;
      const int WM_HOTKEY = 0x0312;

            // Forward hotkey triggers
            if (m.Msg == WM_HOTKEY)
            {
                hotkeyManager.ProcessHotkey((ushort)m.WParam);
                return;
            }

      if (minimizeToTray.Value && 
        m.Msg == WM_SYSCOMMAND && m.WParam.ToInt64() == SC_MINIMIZE) {
        SysTrayHideShow();
      } else if (minimizeOnClose.Value &&
        m.Msg == WM_SYSCOMMAND && m.WParam.ToInt64() == SC_CLOSE) {
        /*
         * Apparently the user wants to minimize rather than close
         * Now we still need to check if we're going to the tray or not
         * 
         * Note: the correct way to do this would be to send out SC_MINIMIZE,
         * but since the code here is so simple,
         * that would just be a waste of time.
         */
        if (minimizeToTray.Value)
          SysTrayHideShow();
        else
          WindowState = FormWindowState.Minimized;
      } else {      
        base.WndProc(ref m);
      }
    }

    private void hideShowClick(object sender, EventArgs e) {
      SysTrayHideShow();
    }

    private void ShowParameterForm(ISensor sensor) {
      ParameterForm form = new ParameterForm();
      form.Parameters = sensor.Parameters;
      form.captionLabel.Text = sensor.Name;
      form.ShowDialog();
    }

    private void treeView_NodeMouseDoubleClick(object sender, 
        TreeNodeAdvMouseEventArgs e) {
        SensorNode node = e.Node.Tag as SensorNode;
        if (node != null && node.Sensor.SensorType == SensorType.Control)
        {
            ControlSettingsForm f = new ControlSettingsForm(node.Sensor.Name, node.Sensor.Control, this, settings);
            f.Show();
        }            
        else if (node != null && node.Sensor != null && 
            node.Sensor.Parameters.Length > 0) {
            ShowParameterForm(node.Sensor);
        }
    }

    private void celsiusMenuItem_Click(object sender, EventArgs e) {
      celsiusMenuItem.Checked = true;
      fahrenheitMenuItem.Checked = false;
      unitManager.TemperatureUnit = TemperatureUnit.Celsius;
    }

    private void fahrenheitMenuItem_Click(object sender, EventArgs e) {
      celsiusMenuItem.Checked = false;
      fahrenheitMenuItem.Checked = true;
      unitManager.TemperatureUnit = TemperatureUnit.Fahrenheit;
    }

    private void sumbitReportMenuItem_Click(object sender, EventArgs e) 
    {
      ReportForm form = new ReportForm();
      form.Report = computer.GetReport();
      form.ShowDialog();      
    }

    private void resetMinMaxMenuItem_Click(object sender, EventArgs e) {
      computer.Accept(new SensorVisitor(delegate(ISensor sensor) {
        sensor.ResetMin();
        sensor.ResetMax();
      }));
    }


        private void MainForm_MoveOrResize(object sender, EventArgs e) {
      if (WindowState != FormWindowState.Minimized) {
        settings.SetValue("mainForm.Location.X", Bounds.X);
        settings.SetValue("mainForm.Location.Y", Bounds.Y);
        settings.SetValue("mainForm.Width", Bounds.Width);
        settings.SetValue("mainForm.Height", Bounds.Height);
      }
    }

    private void resetClick(object sender, EventArgs e) {
      // disable the fallback MainIcon during reset, otherwise icon visibility
      // might be lost 
      systemTray.IsMainIconEnabled = false;
      computer.Close();
      computer.Open();
      // restore the MainIcon setting
      systemTray.IsMainIconEnabled = minimizeToTray.Value;
    }

    private void treeView_MouseMove(object sender, MouseEventArgs e) {
      selectionDragging = selectionDragging &
        (e.Button & (MouseButtons.Left | MouseButtons.Right)) > 0; 

      if (selectionDragging)
        treeView.SelectedNode = treeView.GetNodeAt(e.Location);     
    }

    private void treeView_MouseDown(object sender, MouseEventArgs e) {
      selectionDragging = true;
    }

    private void treeView_MouseUp(object sender, MouseEventArgs e) {
      selectionDragging = false;
    }

    private void serverPortMenuItem_Click(object sender, EventArgs e) {
      new PortForm(this).ShowDialog();
    }

    public HttpServer Server {
      get { return server; }
    }


        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            timer.Interval = (int)numericUpDown1.Value;
            SharedData.UpdateRate = timer.Interval;
            settings.SetValue("refresh_rate", timer.Interval);
        }


        private void lcdFormatText_TextChanged(object sender, EventArgs e)
        {
            lcd.FormatString = lcdFormatText.Text;
        }

        private void editControllerButton_Click(object sender, EventArgs e)
        {
            if (controllerListBox.SelectedItems.Count > 0)
            {

                FanController c = controllerListBox.GetSelectedFanController();
                if (c != null) c.ShowForm();
            }
        }

        private void newControllerButton_Click(object sender, EventArgs e)
        {
            int id = fanControllerManager.GetFreeControllerSlot();
            if (id == FanControllerManager.INVALID_CONTROLLER) return;
            try
            {
                FanController c = new FanController(fanControllerManager.GetFreeControllerSlot(), sensors, settings);

                fanControllerManager.AddController(c);
                controllerListBox.AddItem(c);

                c.ShowForm();
            } catch (Exception)
            {
                MessageBox.Show("Cannot create a new fan controller. No supported fan was found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }            
        }

        private void removeControllerButton_Click(object sender, EventArgs e)
        {
            FanController c = controllerListBox.GetSelectedFanController();
            if (c == null) return;

            if (MessageBox.Show("Do you really want to remove this fan controller?", "Remove", MessageBoxButtons.YesNo) == DialogResult.No) return;

            c.DeleteFromSettings();                        
            c.CloseForm();

            fanControllerManager.Controllers.Remove(c);
            controllerListBox.RemoveItem(c);
            
        }

        private void menuItem4_Click(object sender, EventArgs e)
        {
            new HintsForm();
        }

        private void menuItem8_Click(object sender, EventArgs e)
        {
            new UpdateChecker().Check(true);
        }

        private void copyControllerButton_Click(object sender, EventArgs e)
        {
            FanController c = controllerListBox.GetSelectedFanController();
            if (c == null) return;

            FanController f = fanControllerManager.CloneController(c);
            controllerListBox.AddItem(f);

            f.ShowForm();
        }

        private void menuItem10_Click(object sender, EventArgs e)
        {
            LCDTextForm f = new LCDTextForm(lcd);
            f.Show();
        }
    }
}
