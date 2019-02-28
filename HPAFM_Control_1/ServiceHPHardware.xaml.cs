using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for ControlHPHardware.xaml
    /// </summary>
    public partial class ServiceHPHardware : Window
    {
        InterfaceHPHardware hpInterface;
        InterfacePressureController pcInterface;


        public ServiceHPHardware(InterfaceHPHardware ihp, InterfacePressureController ipc)
        {
            hpInterface = ihp;//these have been pre-initialized by the main window and are ready for comms
            pcInterface = ipc;

            InitializeComponent();
            
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "HPControl Window is open.");
        }

        private void RefreshData_Click(object sender, RoutedEventArgs e)
        {
            int p = -10;

            try
            {
                //p = pcInterface.GetCurrentPressure();
                hpInterface.UpdateMeasurements();
            }catch(Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Exception in UpdateTimer_Tick: " + x.Message);
                StatusText.Text = "HPBox Update error! " + DateTime.Now.ToString("HH:mm:ss");
                StatusText.Background = Brushes.RosyBrown;
                UpdateSetpts.IsEnabled = false;
                return;
            }

            UpdateSetpts.IsEnabled = true;

            hpValvesShut.Text = hpInterface.ChamberValvesOpen ? "Open" : "Closed";
            hpValvesShut.Foreground = hpInterface.ChamberValvesOpen ? Brushes.RosyBrown : Brushes.MediumAquamarine;
            eStopInactive.Text = hpInterface.EmergencySwitchPressed ? "Stop" : "Run";
            eStopInactive.Foreground = hpInterface.EmergencySwitchPressed ? Brushes.RosyBrown : Brushes.MediumAquamarine;
            if (hpInterface.PressureWater < -100)
                WaterPress.Text = "N/C";
            else
                WaterPress.Text = hpInterface.PressureWater.ToString("0000");
            GasPress.Text = p.ToString("0000");
            //double bp = Math.Ceiling(WaterPropsCalculator.GetMinLiquidPresMPa_rel(hpInterface.TempWater) * WaterPropsCalculator.MPatoPSI);
            //BoilPress.Text = bp.ToString("0000");

            switch (hpInterface.HeaterStatus)
            {
                case InterfaceHPHardware.HeaterState.Off:
                    HeaterStatus.Text = "Off";
                    break;
                case InterfaceHPHardware.HeaterState.HeatUp:
                    HeaterStatus.Text = "Heat Up";
                    break;
                case InterfaceHPHardware.HeaterState.CoolDown:
                    HeaterStatus.Text = "Cool Down";
                    break;
                case InterfaceHPHardware.HeaterState.Stabilize:
                    HeaterStatus.Text = "Stabilize";
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
            //double bt = Math.Floor(WaterPropsCalculator.GetMaxLiquidTempC(hpInterface.PressureWater / WaterPropsCalculator.MPatoPSI));
            //BoilTemp.Text = bt.ToString("000");

            ControllerTemp.Text = hpInterface.TempController.ToString("00.0");
            HeaterDuty.Text = hpInterface.HeaterDutyCycle.ToString("000");
            HeaterPower.Text = Math.Floor(hpInterface.HeaterPower).ToString("000");

            StatusText.Text = "Last updated " + DateTime.Now.ToString("HH:mm:ss");
            StatusText.Background = Brushes.MediumAquamarine;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "HPControl Window is closing.");
            //interface de-init will be done by the main window
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

            if(np<0 || np > 3000)
            {
                MessageBox.Show("Out-of-range pressure entered!");
                return;
            }
            

            int nd;//new temp target
            par = int.TryParse(HeaterDutySetpt.Text, out nd);
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
                //pcInterface.SetPressure(np);
                //PressureSetpt.Text = np.ToString("0000"); //maintain 4-zero format
                
                hpInterface.SetTargetWaterTemp((float)nd);
                HeaterDutySetpt.Text = nd.ToString("000"); //maintain 3-zero format
            }catch(Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "UpdateSetpts_Click error: " + x.Message, true);
                return;
            }            

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "New user setpoint for air pressure (PSI): " + PressureSetpt.Text + "; duty cycle (255): " + HeaterDutySetpt.Text);
        }

        private void IllumCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            hpInterface.SetIllumination(IllumCheckbox.IsChecked == true);
        }
    }
}
