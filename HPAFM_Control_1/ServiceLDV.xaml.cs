using FFTWSharp;
using SciChart.Charting.Model.DataSeries;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
    /// Interaction logic for ControlLDV.xaml
    /// </summary>
    public partial class ServiceLDV : Window
    {
        InterfaceLDV ldvInterface;
        InterfaceDataBox dbInterface;
        const int numpts = 2000000;//131072;
        XyDataSeries<double, double> lineData = null, lineData2 = null;
        double[][] LDVdata;
        double[] time, fftd_data;

        GCHandle fft_in, fft_out; //when reserving memory for FFT, ensure it will be unreserved to avoid memory eating
        IntPtr fftPlan;

        public ServiceLDV(InterfaceLDV ildv, InterfaceDataBox dbi)
        {
            ldvInterface = ildv; //this is already initialized by main window
            dbInterface = dbi;

            InitializeComponent();

            ldvInterface.ConfigureVelocityMode(0.02);// 0.5); //0.5 = +-0.5 m/s max
            //dbInterface.setPiezoStatic(0); //full approach position

            LDVdata = new double[2][];
            LDVdata[0] = new double[numpts];
            LDVdata[1] = new double[numpts];

            //fftd_data = new double[numpts + 2];
            //fft_in = GCHandle.Alloc(LDVdata[1], GCHandleType.Pinned); //must be freed later
            //fft_out = GCHandle.Alloc(fftd_data, GCHandleType.Pinned); //must be freed later
            //fftPlan = fftw.dft_r2c_1d(LDVdata[1].Length, fft_in.AddrOfPinnedObject(), fft_out.AddrOfPinnedObject(), fftw_flags.Estimate);

            time = new double[numpts];

            for (int i = 0; i < numpts; i++)
            {
                time[i] = i * (1e-6);
            }
            
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "LDV Control Window is open.");
        }        

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "LDV Control Window is closing.");
            //interface de-init will be done by main window

            fftw.destroy_plan(fftPlan);
            fft_in.Free();
            fft_out.Free();
        }

        private void DisplacementMode_Click(object sender, RoutedEventArgs e)
        {
            ldvInterface.ConfigureDisplacementMode(2e-5); //2e-5 = +-20 um max
        }

        private void VelocityMode_Click(object sender, RoutedEventArgs e)
        {
            ldvInterface.ConfigureVelocityMode(0.5); //0.5 = +-0.5 m/s max
        }

        private void GetData_Click(object sender, RoutedEventArgs e)
        {
            //dbInterface.setPiezoStatic(7);//retract to start
            //System.Threading.Thread.Sleep(1500);//wait for retract to complete
            ldvInterface.GetDataOnce(LDVdata, PlotData);
            //System.Threading.Thread.Sleep(200);
            //dbInterface.setPiezoStatic(0); //approach piezo fully while measuring FD curve
            //System.Threading.Thread.Sleep(1000);
            //dbInterface.setPiezoStatic(7);
        }

        private void PlotData()
        {
            //dbInterface.setPiezoStatic(0); //return to touching position
            lineData = new XyDataSeries<double, double>();
            lineData.InsertRange(0, time, LDVdata[0]);

            lineData2 = new XyDataSeries<double, double>();
            lineData2.InsertRange(0, time, LDVdata[1]);

            sciChartSurface.Dispatcher.Invoke(() =>
            {
                LineSeries.DataSeries = lineData;
                //LineSeries2.DataSeries = lineData2;
                //sciChartSurface.ZoomExtents();
            });
        }

        private void StartLDV_Click(object sender, RoutedEventArgs e)
        {
            HPAFMLogger.WriteFDPoints3(LDVdata[0]);
            //HPAFMLogger.WriteFDPoints3(LDVdata[1]);
            //HPAFMLogger.WriteFDFooter();
            return;
            throw new NotImplementedException();
            lineData = new XyDataSeries<double, double>();
            LineSeries.DataSeries = lineData; //Plot data gathered so far
            ldvInterface.GetDataContinuous(LDVdata, LDVReaderProgress);
        }

        private void LDVReaderProgress()
        {
            //tuna1[tuna_ptr] = DateTime.Now.Ticks;


            fftw.execute(fftPlan); //do FFT transform from ldv_fft_data to fftd_data
            //do fake conversion to amplitudes to time it
            for (int f = 0; f < fftd_data.Length; f++)
            {
                fftd_data[f] = Math.Sqrt(fftd_data[f] * fftd_data[f] + fftd_data[f] * fftd_data[f]) * 2.0 / (fftd_data.Length - 2); //amplitude of FFT (element 0 and n need to be not multiplied by 2 for accurate amplitudes due to folding of positive and negative spectrum here)
            }

            dbInterface.setkHzwave(true); //testing update rate
            //lineData.Clear();
            //lineData.Append(time, LDVdata[1]);

            //tuna2[tuna_ptr] = DateTime.Now.Ticks;
            //tuna_ptr++;
            //if (tuna_ptr == tuna2.Length)
            //    tuna_ptr = 0;
        }

        private void StopLDV_Click(object sender, RoutedEventArgs e)
        {
            ldvInterface.StopDataContinuous();
        }

        private void FreqSelect_DragEnded(object sender, EventArgs e)
        {
            LDVOutText.Text = FreqSelect.X1.ToString();
        }

        private void FreqSelectBox_DragEnded(object sender, EventArgs e)
        {
            //LDVOutText.Text = FreqSelectBox.X1.ToString() + ", " + FreqSelectBox.X2.ToString();
        }

        private void HeightSelect_DragEnded(object sender, EventArgs e)
        {
            if(PointSeries.DataSeries != null)
                PointSeries.DataSeries.Clear(); //reset any points drawn on screen

            if (LineSeries.DataSeries == null)
                return; //nothing to analyze

            //analyze for Ping using the threshold of this line as limit
            IList<double> y = (IList<double>)LineSeries.DataSeries.YValues;
            IList<double> x = (IList<double>)LineSeries.DataSeries.XValues;

            int n = y.Count() - 1;
            double thr = (double)HeightSelect.Y1; //height of moved line

            while(y[n] < thr && n > 0)
            {
                n--;
            }

            if (n == 0)
                return; //unsuccessful analysis

            XyDataSeries<double,double> pointData = new XyDataSeries<double, double>();
            pointData.AcceptsUnsortedData = true;

            int ping_end = n;
            pointData.Append(x[n], y[n]); //highlight this end point
            PointSeries.DataSeries = pointData; //plot for now

            int width = 100; //if threshold not crossed regularly every 100 points (~ sample rate / resonant frequency) assume the ping has ended
            int w = 0; //measured width of not crossing threshold

            while(w<width && n > 0)
            {
                n--;
                if (y[n] > thr)
                {
                    w = 0;
                }
                else
                {
                    w++;
                }
            }

            if (n == 0)
                return; //unsuccessful analysis

            int ping_start = n;
            pointData.Append(x[n], y[n]);//highlight this start point
            int n0, n1;
            double mex;

            while (y[n] > -thr && n < ping_end)
            {
                n++;
            } //find first downward slope
            n0 = n;
            if (n == ping_end)
                return; //unsuccessful analysis

            for (int p = 0; p < 5; p++) //find 5 first extreme points in a latching amplifier approach (between crossing of -thr and +thr) - if this can be done then it is safe to assume this is a real ping (can be further checked by periodicity between extrema)
            {
                while (y[n] < thr && n < ping_end)
                {
                    n++;
                } //find next upward slope
                n1 = n;
                if (n == ping_end)
                    return; //unsuccessful analysis
                mex = 0;
                for(int mn=n0; mn<n1; mn++) //find minimum between n0 and n1
                {
                    if (y[mn] < mex)
                    {
                        mex = y[mn];
                        n0 = mn;
                    }
                }
                pointData.Append(x[n0], y[n0]); //highlight minimum point

                while (y[n] > -thr && n < ping_end)
                {
                    n++;
                } //find next downward slope
                n0 = n;
                if (n == ping_end)
                    return; //unsuccessful analysis

                mex = 0;
                for (int mn = n1; mn < n0; mn++) //find maximum between n1 and n0
                {
                    if (y[mn] > mex)
                    {
                        mex = y[mn];
                        n1 = mn;
                    }
                }

                pointData.Append(x[n1], y[n1]); //highlight maximum point
            }

            LDVOutText.Text = "Analysis Finished!";
        }
    }
}
