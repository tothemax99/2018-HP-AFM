using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        InterfaceThorCamera camInterface;
        InterfaceThorMotorInertial inertialMotorInterface;
        InterfaceThorMotorLinear linearMotorInterface;
        InterfacePIMotors piMotorsInterface;
        InterfacePressureController pcInterface;
        InterfaceHPHardware hpInterface;
        InterfaceLDV ldvInterface;
        InterfaceDataBox dbInterface;

        IntPtr displayHandle = IntPtr.Zero;

        Window camService, hpService, motorsService, ldvService, dbService;

        ControlFD fdController;
        ControlEnvironment envController;
        ControlAutomation autoController;
        ControlCamera camController;
        ControlMotors motorController;

        #region Initialization
        public MainWindow()
        {
            InitializeComponent();

            //HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Hello! Starting new HPAFM session.");

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            camInterface = new InterfaceThorCamera();
            inertialMotorInterface = new InterfaceThorMotorInertial();
            linearMotorInterface = new InterfaceThorMotorLinear();
            piMotorsInterface = new InterfacePIMotors();
            pcInterface = new InterfacePressureController();
            hpInterface = new InterfaceHPHardware();
            ldvInterface = new InterfaceLDV();
            dbInterface = new InterfaceDataBox();

            if (Properties.Settings.Default.ServiceMode)
            {
                ServiceTab.IsEnabled = true;
                ServiceTab.Focus();
                //ExperimentTab.IsEnabled = false;
            }
            else
            {
                ServiceTab.Visibility = Visibility.Hidden;
                ExperimentTab.IsEnabled = true;
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception x = (Exception)e.ExceptionObject;
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "Global unhandled exception: " + x.Message);
            if(x.InnerException != null)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "Inner exception of above: " + x.InnerException.Message);
            }
            //Application will now terminate
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Main Window closing...");

            if (hpService != null && hpService.IsLoaded)
                hpService.Close();

            if (camService != null && camService.IsLoaded)
                camService.Close();

            if (motorsService != null && motorsService.IsLoaded)
                motorsService.Close();

            if (ldvService != null && ldvService.IsLoaded)
                ldvService.Close();

            if (dbService != null && dbService.IsLoaded)
                dbService.Close();


            if (envController != null)
                envController.StopUpdates();

            if (camController != null)
            {
                camController.SetIllumination(false);
                camController.StopCamera();
            }

            /* test whether this causes crash on HPAFM computer
            camInterface.Exit();
            inertialMotorInterface.Exit();
            linearMotorInterface.Exit();
            piMotorsInterface.Exit();
            pcInterface.Exit();
            hpInterface.Exit();
            ldvInterface.Exit();
            dbInterface.Exit();
            */

            linearMotorInterface.Exit();

            Properties.Settings.Default.Save(); //save user settings for next time

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "All interfaces closed. Goodbye.");
        }
        #endregion

        #region Tab 1 - Experiment Definition/startup
        private void OutFile_Click(object sender, RoutedEventArgs e)
        {
            string fn = HPAFMLogger.OutputFileSelect(ExpName.Text);
            if (fn != null)
            {
                OutFileBtn.Content = System.IO.Path.GetFileName(fn); ;
                InitHardwareBtn.IsEnabled = true;
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Output filename selected: " + fn);
            }
            else
            {
                return;
            }
        }

        private async void InitHardware_Click(object sender, RoutedEventArgs e)
        {
            Button me = (Button)sender;
            me.IsEnabled = false;
            OutFileBtn.IsEnabled = false;

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Beginning hardware interface initialization");

            try
            {
                me.Content = "Camera";
                camInterface.InitializeCamera();
                me.Content = "Inertial";
                await System.Threading.Tasks.Task.Run(() => inertialMotorInterface.InitializeMotorInertial());
                me.Content = "Linear";
                await System.Threading.Tasks.Task.Run(() => linearMotorInterface.InitializeMotorLinear());
                me.Content = "PI";
                await System.Threading.Tasks.Task.Run(() => piMotorsInterface.InitializePIMotors());
                me.Content = "Pressure";
                await System.Threading.Tasks.Task.Run(() => pcInterface.InitializePressureController());
                me.Content = "HPBox";
                await System.Threading.Tasks.Task.Run(() => hpInterface.InitializeHPController());
                me.Content = "LDV";
                await System.Threading.Tasks.Task.Run(() => ldvInterface.InitializeLDV());
                me.Content = "DataBox";
                await System.Threading.Tasks.Task.Run(() => dbInterface.InitializeDataBox());
                me.Content = "Done!";
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "Exception in hardware initialization: " + x.Message, true);
                if(x.InnerException != null)
                {
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "Inner Exception in hardware initialization: " + x.InnerException.Message);
                }
                //Close();
                return;
            }

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Interface initialization has completed successfully.");

            camController = new ControlCamera(camInterface, linearMotorInterface, hpInterface);
            CameraControllerFrame.Navigate(camController);
            camController.SetIllumination(true); //turn on camera illumination light
            camController.StartCamera(true);

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Camera controller opened");

            motorController = new ControlMotors(piMotorsInterface, inertialMotorInterface);
            MotorsControllerFrame.Navigate(motorController);

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Motors controller opened");

            envController = new ControlEnvironment(hpInterface, pcInterface);
            EnvControllerFrame.Navigate(envController);
            envController.StartUpdates();

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Environment controller opened");

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Hardware initialization completed");

            Cont1Btn.IsEnabled = true;
        }

        private void Continue1_Click(object sender, RoutedEventArgs e)
        {
            HPAFMLogger.WriteHeader(ExpName.Text, SmpName.Text, PrbName.Text);
            //ExperimentTab.IsEnabled = false;
            MotorTab.IsEnabled = true;
            MotorTab.Focus();
            
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Experiment definition completed, moving to Motor Limits tab");
        }
        #endregion

        #region Tab 2 - Motor Limits/alignment
        
        private void Continue2_Click(object sender, RoutedEventArgs e)
        {
            if (!motorController.CheckBasisEstablished) //operator has to manually set basis by using buttons in MotorsController page
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Cannot continue to next tab until PI motor sample and probe basis are established.", true);
                return;
            }

            fdController = new ControlFD(dbInterface, ldvInterface);
            FDControllerFrame.Navigate(fdController);

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "FD controller opened");

            //MotorTab.IsEnabled = false;
            LDVTab.IsEnabled = true;
            LDVTab.Focus();
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Moving to LDV Lock-In tab");
        }
#endregion

        #region Tab 3 - LDV Lock-In set FFT properties
        private void CheckProbe_Click(object sender, RoutedEventArgs e)
        {
            //fdController.SetProbeFFTNums(double.Parse(ResonantFreq.Text), double.Parse(MinResonantLevel.Text), double.Parse(NoiseFreq.Text), double.Parse(MaxNoiseLevel.Text));

            ControlFD.ProbeStatus ps = fdController.CheckProbeTouching();

            if (ps == ControlFD.ProbeStatus.ProbeFree)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Probe free is detected");

                autoController = new ControlAutomation(motorController, envController, fdController);
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Automation Controller is opened");

                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Moving to Approach tab");

                ApproachTab.IsEnabled = true;
                ApproachTab.Focus();
            }
            else
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Probe status was not detected: " + ps.ToString(), true);
            }
        }
        #endregion

        #region Tab 4 - Approach sample and set limits
        private void AutoApproach_Click(object sender, RoutedEventArgs e)
        {
            //AutoApproachSBtn.IsEnabled = false;
            //PLLScanBtn.IsEnabled = false;
            //AutoApproachCBtn.IsEnabled = true;
            //RetractABtn.IsEnabled = false;

            try
            {
                autoController.StartAutoApproach();
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Starting auto approach");
            }catch(Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "AutoApproach_Click error: " + x.Message, true);
            }
        }

        private void AutoApproachCancel_Click(object sender, RoutedEventArgs e)
        {
            //AutoApproachCBtn.IsEnabled = false;
            //AutoApproachSBtn.IsEnabled = true;
            //RetractABtn.IsEnabled = true;
            autoController.CancelAutoApproach();
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Canceling auto approach by operator input");
        }


        private void AutoApproachLimit_Click(object sender, RoutedEventArgs e)
        {
            motorController.SetProbeTouchingLimit();
            Continue3.IsEnabled = true;
        }

        private void Continue3_Click(object sender, RoutedEventArgs e)
        {
            if (!motorController.CheckApproachCompleted)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Approach was not completed successfully, cannot continue", true);
                return;
            }

            //LDVTab.IsEnabled = false;
            FDTab.IsEnabled = true;
            FDTab.Focus();
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "LDV setup and auto-approach completed, moving to FD setup tab");
        }
        #endregion

        #region Tab 4 - FD manual setup of amplitude and trigger
        private void Continue4_Click(object sender, RoutedEventArgs e)
        {
            if (!fdController.FDanalysisSuccessful)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "FD analysis was not completed successfully, cannot continue", true);
                return;
            }

            motorController.ProbeRetractFromSample(); //all subsequent operations expect to start in the withdrawn position
            AutomationControllerFrame.Navigate(autoController); //enable auto controller actions like measure FD

            AutoTab.IsEnabled = true;
            AutoTab.Focus();
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "All setup completed, moving to automation tab");
        }


        #endregion

        #region Tab 5 - Automation FD controls
        private void AutomationLoad_Click(object sender, RoutedEventArgs e)
        {
            string fn = HPAFMReader.InputFileSelect(ExpName.Text);
            double time = -1;
            if (fn != null)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "AutomationLoad_Click: loading file " + fn);
                try
                {
                    time = HPAFMReader.ProcessInputFile();
                }
                catch (Exception x)
                {
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "AutomationLoad_Click: unable to parse input file: " + x.Message, true);
                    return;
                }
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "AutomationLoad_Click: parsed file successfully");
                AutomationStartBtn.IsEnabled = true;
                AutomationStatusBox.Text = "Loaded " + HPAFMReader.loadedAutomation.Count().ToString() + " commands from " + fn + ", expected run time is " + time.ToString() + " seconds";
            }
            else
            {
                return;
            }
        }

        private void AutomationStart_Click(object sender, RoutedEventArgs e)
        {
            if (HPAFMReader.loadedAutomation == null)
            {//no data has been loaded
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "AutomationStart_Click: no automation data loaded");
                return;
            }
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "AutomationStart_Click: starting automation");
            //AutomationLoadBtn.IsEnabled = false; //disable more loading
            AutomationStartBtn.IsEnabled = false;
            AutomationCancelBtn.IsEnabled = true;
            autoController.StartAutomation(HPAFMReader.loadedAutomation);
        }

        private void AutomationCancel_Click(object sender, RoutedEventArgs e)
        {
            AutomationLoadBtn.IsEnabled = true;
            AutomationStartBtn.IsEnabled = true;
            AutomationCancelBtn.IsEnabled = false;
            autoController.CancelAutomation();
        }

        #endregion

        #region Tab 5 - Service Controls
        private void ServiceBtn_Click(object sender, RoutedEventArgs e)
        {
            Button s = (Button)sender;
            string d = (string)s.Tag;

            try
            {
                switch (d)
                {
                    case "camera":
                        if(camService == null)
                        {
                            camInterface.InitializeCamera();

                            /*System.Windows.Forms.PictureBox picturebox1 = new System.Windows.Forms.PictureBox();
                            WFHost.Child = picturebox1;
                            displayHandle = picturebox1.Handle;*/

                            camService = new ServiceCamera(camInterface);
                            camService.Show();
                        }
                        break;
                    case "databox":
                        if(dbService == null)
                        {
                            dbInterface.InitializeDataBox();

                            dbService = new ServiceDataBox(dbInterface);
                            dbService.Show();
                        }
                        break;
                    case "ldv":
                        if(ldvService == null)
                        {
                            ldvInterface.InitializeLDV();
                            //ldvInterface.ConfigureVelocityMode(0.5);

                            //dbInterface.InitializeDataBox();

                            ldvService = new ServiceLDV(ldvInterface, dbInterface);
                            ldvService.Show();
                        }
                        break;
                    case "motors":
                        if(motorsService == null)
                        {
                            //inertialMotorInterface.InitializeMotorInertial();
                            linearMotorInterface.InitializeMotorLinear();
                            /*piMotorsInterface.InitializePIMotors();

                            if (!linearMotorInterface.IsHomed)
                            {
                                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Thorlabs Linear Motor homing is required");
                                MessageBoxResult hy = MessageBox.Show("Thorlabs Linear Motor (camera focus) has not been homed. Perform homing now? This will move camera back to 0mm.", "Homing", MessageBoxButton.OKCancel);
                                if (hy == MessageBoxResult.OK)
                                {
                                    try
                                    {
                                        linearMotorInterface.HomeMotor();
                                    }catch(Exception x)
                                    {
                                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Unable to home thorlabs linear motor: " + x.Message, true);
                                        return;
                                    }
                                }
                                else
                                {
                                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Operator cancelled thorlabs linear motor homing.");
                                    return;
                                }
                            }
                            if (!piMotorsInterface.IsMotorHomed(1))
                            {
                                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "PI Motor 1 homing is required");
                                MessageBoxResult hy = MessageBox.Show("PI Motor 1 (probe axis) has not been homed. Perform homing now? Ensure the collar is NOT connected at this point.", "Homing", MessageBoxButton.OKCancel);
                                if (hy == MessageBoxResult.OK)
                                {
                                    try
                                    {
                                        piMotorsInterface.HomeMotor(1, InterfacePIMotors.HomingType.Negative, true);
                                    }catch(Exception x)
                                    {
                                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Unable to home PI 1 linear motor: " + x.Message, true);
                                        return;
                                    }
                                }
                                else
                                {
                                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Operator cancelled PI Motor 1 homing.");
                                    return;
                                }
                            }
                            if (!piMotorsInterface.IsMotorHomed(2))
                            {
                                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "PI Motor 2 homing is required");
                                MessageBoxResult hy = MessageBox.Show("PI Motor 2 (sample axis) has not been homed. Perform homing now? Ensure the collar is NOT connected at this point.", "Homing", MessageBoxButton.OKCancel);
                                if (hy == MessageBoxResult.OK)
                                {
                                    try
                                    {
                                        piMotorsInterface.HomeMotor(2, InterfacePIMotors.HomingType.Negative, true);
                                    }
                                    catch (Exception x)
                                    {
                                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Unable to home PI 2 linear motor: " + x.Message, true);
                                        return;
                                    }
                                }
                                else
                                {
                                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Operator cancelled PI Motor 2 homing.");
                                    return;
                                }
                            }*/

                            motorsService = new ServiceMotion(piMotorsInterface, inertialMotorInterface, linearMotorInterface);
                            motorsService.Show();
                        }
                        break;
                    case "hpbox":
                        if (hpService == null)
                        {
                            pcInterface.InitializePressureController();
                            hpInterface.InitializeHPController();

                            /*hpService = new ServiceHPHardware(hpInterface, pcInterface);
                            hpService.Show();*/

                            envController = new ControlEnvironment(hpInterface, pcInterface);
                            EnvControllerFrame.Navigate(envController);
                            envController.StartUpdates();
                        }
                        break;
                }
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "ServiceBtn_Click: unable to perform action: " + x.Message, true);
            }
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            HPAFMLogger.WriteFDHeader(motorController.CurrentSampleBasis, motorController.CurrentProbeBasis, envController.WaterPressure, envController.WaterTemperature, 0);
            HPAFMLogger.WriteFDPoints5(fdController.MinText.Text);
            HPAFMLogger.WriteFDFooter();
            return;


            camInterface.InitializeCamera();
            linearMotorInterface.InitializeMotorLinear();
            hpInterface.InitializeHPController();
            pcInterface.InitializePressureController();
            camController = new ControlCamera(camInterface, linearMotorInterface, hpInterface);
            CameraControllerFrame.Navigate(camController);
            camController.SetIllumination(true); //turn on camera illumination light
            camController.StartCamera(true);

            envController = new ControlEnvironment(hpInterface, pcInterface);
            EnvControllerFrame.Navigate(envController);
            envController.StartUpdates();

            /*fdController = new ControlFD(dbInterface, ldvInterface);
            FDControllerFrame.Navigate(fdController);*/
        }

        #endregion
    }
}
