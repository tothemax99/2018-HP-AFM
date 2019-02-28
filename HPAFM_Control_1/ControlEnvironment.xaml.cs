using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for EnvironmentController.xaml
    /// </summary>
    public partial class ControlEnvironment : Page
    {
        InterfaceHPHardware hpInterface;
        InterfacePressureController pcInterface;
        DispatcherTimer updateTimer;

        public double WaterTemperature { get { return hpInterface.TempWater; } }
        public double WaterPressure {  get { return hpInterface.PressureWater; } }

        public ControlEnvironment(InterfaceHPHardware ihp, InterfacePressureController ipc)
        {
            hpInterface = ihp;//these have been pre-initialized by the main window and are ready for comms
            pcInterface = ipc;

            InitializeComponent();

            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromSeconds(Properties.Settings.Default.EnvTimerInterval);
            updateTimer.Tick += UpdateTimer_Tick;

            float tempSetpt = hpInterface.GetTargetWaterTemp();
            int pressSetpt = pcInterface.GetSetpt();

            PressureSetpt.Text = pressSetpt.ToString("0000");
            WaterTempSetpt.Text = tempSetpt.ToString("000");
        }

        public void StartUpdates()
        {
            if(updateTimer.IsEnabled)
            {
                throw new ApplicationException("StartUpdates: cannot start update timer as it is already running.");
            }

            updateTimer.Start();

            UpdateSetpts.IsEnabled = true;
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            int p = -10;

            try
            {
                p = pcInterface.GetCurrentPressure();
                hpInterface.UpdateMeasurements();
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Exception in UpdateTimer_Tick: " + x.Message);
                StatusText.Text = "HPBox Update error! " + DateTime.Now.ToString("HH:mm:ss");
                StatusText.Background = Brushes.RosyBrown;
                //StopUpdates();
                return;
            }

            hpValvesShut.Text = hpInterface.ChamberValvesOpen ? "Open" : "Closed";
            hpValvesShut.Foreground = hpInterface.ChamberValvesOpen ? Brushes.RosyBrown : Brushes.MediumAquamarine;
            eStopInactive.Text = hpInterface.EmergencySwitchPressed ? "Stop" : "Run";
            eStopInactive.Foreground = hpInterface.EmergencySwitchPressed ? Brushes.RosyBrown : Brushes.MediumAquamarine;
            if (hpInterface.PressureWater < -100)
                WaterPress.Text = "N/C";
            else
                WaterPress.Text = hpInterface.PressureWater.ToString("0000");
            GasPress.Text = p.ToString("0000");
            try
            {
                double bp = Math.Ceiling(WaterPropsCalculator.GetMinLiquidPresMPa_rel(hpInterface.TempWater) * WaterPropsCalculator.MPatoPSI);
                BoilPress.Text = bp.ToString("0000");
            }
            catch (ArgumentException)
            {
                BoilPress.Text = "???";
            }

            switch (hpInterface.HeaterStatus)
            {
                case InterfaceHPHardware.HeaterState.Off:
                    HeaterStatus.Text = "Off";
                    HeaterStatus.Foreground = Brushes.MediumAquamarine;
                    break;
                case InterfaceHPHardware.HeaterState.HeatUp:
                    HeaterStatus.Text = "Heat Up";
                    HeaterStatus.Foreground = Brushes.MediumVioletRed;
                    break;
                case InterfaceHPHardware.HeaterState.CoolDown:
                    HeaterStatus.Text = "Cool Down";
                    HeaterStatus.Foreground = Brushes.DodgerBlue;
                    break;
                case InterfaceHPHardware.HeaterState.Stabilize:
                    HeaterStatus.Text = "Stabilize";
                    HeaterStatus.Foreground = Brushes.Magenta;
                    break;
                case InterfaceHPHardware.HeaterState.Stable:
                    HeaterStatus.Text = "Stable";
                    HeaterStatus.Foreground = Brushes.DarkViolet;
                    break;
            }

            switch (hpInterface.TempWaterError)
            {
                case InterfaceHPHardware.ThermocoupleError.OK:
                    WaterTemp.Text = hpInterface.TempWater.ToString("000");
                    break;
                case InterfaceHPHardware.ThermocoupleError.OpenCircuit:
                    WaterTemp.Text = "o" + hpInterface.TempWater.ToString("000");
                    break;
                case InterfaceHPHardware.ThermocoupleError.ShortGND:
                    WaterTemp.Text = "g" + hpInterface.TempWater.ToString("000");
                    break;
                case InterfaceHPHardware.ThermocoupleError.ShortVcc:
                    WaterTemp.Text = "v" + hpInterface.TempWater.ToString("000");
                    break;
            }
            switch (hpInterface.TempSurfaceError)
            {
                case InterfaceHPHardware.ThermocoupleError.OK:
                    SurfaceTemp.Text = hpInterface.TempSurface.ToString("000");
                    break;
                case InterfaceHPHardware.ThermocoupleError.OpenCircuit:
                    SurfaceTemp.Text = "o" + hpInterface.TempSurface.ToString("000");
                    break;
                case InterfaceHPHardware.ThermocoupleError.ShortGND:
                    SurfaceTemp.Text = "g" + hpInterface.TempSurface.ToString("000");
                    break;
                case InterfaceHPHardware.ThermocoupleError.ShortVcc:
                    SurfaceTemp.Text = "v" + hpInterface.TempSurface.ToString("000");
                    break;
            }

            try
            {
                double bt = Math.Floor(WaterPropsCalculator.GetMaxLiquidTempC(hpInterface.PressureWater / WaterPropsCalculator.MPatoPSI));
                BoilTemp.Text = bt.ToString("000");
            }
            catch (ArgumentException)
            {
                BoilTemp.Text = "???";
            }

            ControllerTemp.Text = hpInterface.TempController.ToString("00.0");
            HeaterDuty.Text = hpInterface.HeaterDutyCycle.ToString("000");
            HeaterPower.Text = Math.Floor(hpInterface.HeaterPower).ToString("000");

            StatusText.Text = "Last updated \r\n" + DateTime.Now.ToString("HH:mm:ss");
            StatusText.Background = Brushes.MediumAquamarine;
        }

        public void StopUpdates()
        {
            if (!updateTimer.IsEnabled)
            {
                throw new ApplicationException("StopUpdates: cannot stop update timer as it is already stopped.");
            }

            updateTimer.Stop();
            UpdateSetpts.IsEnabled = false;
        }

        private void UpdateSetpts_Click(object sender, RoutedEventArgs e)
        {
            if (hpInterface.ChamberValvesOpen)
            {
                MessageBox.Show("Cannot update setpt with chamber valves open!");
                return;
            }

            if (hpInterface.EmergencySwitchPressed)
            {
                MessageBox.Show("Cannot update setpt with e-stop active!");
                return;
            }

            bool par;

            int np;//new pressure setpt
            par = int.TryParse(PressureSetpt.Text, out np);
            if (!par)
            {
                MessageBox.Show("Invalid pressure entered!");
                return;
            }

            if (np < 0 || np > 3000)
            {
                MessageBox.Show("Out-of-range pressure entered!");
                return;
            }


            int nd;//new temp target
            par = int.TryParse(WaterTempSetpt.Text, out nd);
            if (!par)
            {
                MessageBox.Show("Invalid temp target entered!");
                return;
            }

            if (nd < 20 || nd > 300)
            {
                MessageBox.Show("Out-of-range temp target entered!");
                hpInterface.TurnOffHeater();
                return;
            }

            try
            {
                pcInterface.SetPressure(np);
                PressureSetpt.Text = np.ToString("0000"); //maintain 4-zero format

                hpInterface.SetTargetWaterTemp((float)nd);
                WaterTempSetpt.Text = nd.ToString("000"); //maintain 3-zero format
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "UpdateSetpts_Click error: " + x.Message, true);
                return;
            }

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "New user setpoint for air pressure (PSI): " + PressureSetpt.Text + "; duty cycle (255): " + WaterTempSetpt.Text);

        }

        public void SetTarget(double t_celsius, double p_psi_rel)
        {
            throw new NotImplementedException();
        }
    }
}
