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
using System.Windows.Shapes;

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for ControlDataBox.xaml
    /// </summary>
    public partial class ServiceDataBox : Window
    {
        InterfaceDataBox dbInterface;

        public ServiceDataBox(InterfaceDataBox db)
        {
            InitializeComponent();

            dbInterface = db;//initialized already by main window and ready for comms

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "DBControl Window is open.");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "DBControl Window is closing.");
            //interface de-init will be done by the main window
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {// turn on 1kHz wave
            dbInterface.setkHzwave(true);
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            dbInterface.setkHzwave(false);
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            float piezo_volt = (float)((Slider)sender).Value;
            dbInterface.setPiezoStatic(piezo_volt);
            PiezoVolt.Text = piezo_volt.ToString() + "V";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            int amplitude;
            float phase;
            dbInterface.doPLLscan(out phase, out amplitude);
            PhaseText.Text = phase.ToString();
            PAmplText.Text = amplitude.ToString();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            int ampl=dbInterface.getPLLampl();
            AmplText.Text = ampl.ToString();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            List<InterfaceDataBox.FDPoint> fdlist = dbInterface.doFD(10, 0);
            if (fdlist == null)
            {
                DataText.Text = "Null Result";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                for(int i=0; i<fdlist.Count; i++)
                {
                    sb.Append(fdlist[i].voltage);
                    sb.Append(',');
                    sb.Append(fdlist[i].pll_ampl);
                    sb.AppendLine();
                }
                DataText.Text = sb.ToString();
            }
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            List<InterfaceDataBox.PiezoCalPoint> pclist = dbInterface.doPiezoCal(10, 0);
            //DataText.Text = pclist.ToString();
        }
    }
}
