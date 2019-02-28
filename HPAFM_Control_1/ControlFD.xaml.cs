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
using System.Threading;
using System.ComponentModel;
using SciChart.Charting.Visuals;
using System.Runtime.InteropServices;
using FFTWSharp;
using SciChart.Charting.Model.DataSeries;

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for FDController.xaml
    /// </summary>
    public partial class ControlFD : Page
    {
        InterfaceDataBox dbInterface;
        InterfaceLDV ldvInterface;

        //double[][] ldv_fd_data; //data store for LDV 1 second, 2 channels
        //double[] fd_time; //time to plot above data (constant)

        double[][] ldv_fft_data; //data store for LDV 0.1 second, 2 channels
        double[][] ldv_fft_data_old; //old buffer for analysis
        double[][] ldv_fft_data_long;
        double[] ldv_time; //time to plot above data (constant)
        double[] ldv_time_old; //time to plot above data (constant)
        double[] ldv_time_long;
        double[] fftd_data; //data after FFT has been called, complex format [re][im]
        double[] fftd_data_old; //a secondary buffer to ensure the ping signal is captured by measuring when probe is released, complex format [re][im]
        double[] fftd_amplitudes; //amplitude only of FFT data
        double[] fftd_frequencies; //frequencies to plot above amplitudes (constant)

        double min_fd_result = 0;
        int min_fd_counter = 0;

        const int fft_pts = 131072; //131072 ensures decent alignment with hardware data transfer from LDV
        const int long_mult = 10;
        const double fft_df = 1.0e6 / fft_pts; //max frequency is nyquist frequency

        public enum ProbeStatus { ProbeFree, ProbeTouching, NoSignal };
        public bool FFTReady { get { return destroyHandles; } }

        public bool FDanalysisSuccessful = false;
        double[] ping_time, ping_amp;
        
        GCHandle fft_in, fft_out; //when reserving memory for FFT, ensure it will be unreserved to avoid memory eating
        IntPtr fftPlan;
        bool destroyHandles = false;

        public delegate void LiveDataReceived(ProbeStatus ps);
        LiveDataReceived liveDataReceived;

        public ControlFD(InterfaceDataBox idb, InterfaceLDV ildv)
        {
            //All are pre-initialized by caller
            dbInterface = idb;
            ldvInterface = ildv;

            InitializeComponent();

            NoiseFreqBox.X1 = Properties.Settings.Default.FFTNoiseFreq - 500;
            NoiseFreqBox.X2 = Properties.Settings.Default.FFTNoiseFreq + 500;
            NoiseFreqBox.Y1 = -10;
            NoiseFreqBox.Y2 = Properties.Settings.Default.FFTNoiseLevel;

            ResonantFreqBox.X1 = Properties.Settings.Default.FFTProbeFreq - 500;
            ResonantFreqBox.X2 = Properties.Settings.Default.FFTProbeFreq + 500;
            ResonantFreqBox.Y1 = -10;
            ResonantFreqBox.Y2 = Properties.Settings.Default.FFTProbeLevel;

            NoiseText.Text = "Noise = " + Properties.Settings.Default.FFTNoiseFreq.ToString("00000") + " Hz " + Properties.Settings.Default.FFTNoiseLevel.ToString("0.0e0") + " mm/s";
            ResonantText.Text = "Resonant = " + Properties.Settings.Default.FFTProbeFreq.ToString("00000") + " Hz " + Properties.Settings.Default.FFTProbeLevel.ToString("0.0e0") + " mm/s";

            FDTrigSelect.Y1 = Properties.Settings.Default.FDTrigLevel;

            FDTrigText.Text = "FD Level = " + Properties.Settings.Default.FDTrigLevel.ToString("0.0e0") + " mm/s";

            foreach(ComboBoxItem i in LDVRSelect.Items)
            {
                if(double.Parse((string)i.Tag) == Properties.Settings.Default.LDVVelRange)
                {
                    i.IsSelected = true;
                    break;
                }
            }

            ConfigureLDVRange();

            foreach (ComboBoxItem i in LDVFRSelect.Items)
            {
                if (int.Parse((string)i.Tag) == Properties.Settings.Default.LDVFreqRange)
                {
                    i.IsSelected = true;
                    break;
                }
            }

            ConfigureFFTBuffers();
        }

        ~ControlFD()
        {
            if (destroyHandles)
            {
                //fftw.export_wisdom_to_filename("wisdom.wsd");
                fftw.destroy_plan(fftPlan);
                fft_in.Free();
                fft_out.Free();
            }
        }

        private void ConfigureLDVRange()
        {
            ldvInterface.ConfigureVelocityMode(Properties.Settings.Default.LDVVelRange);
        }

        private void ConfigureFFTBuffers()
        {
            //Freq range to sample rate mapping: 10000 > 400000, 20000 > 1000000, 50000 > 2000000
            //Want to be able to store 1s of data for FD ping analysis, and 0.1s of data for FFT probe-contact analysis
            //initialize output arrays and FFT processor accordingly

            switch (Properties.Settings.Default.LDVFreqRange)
            {
                case 10000:
                    //sample rate = 400 kHz, 1 s of data = 400k samples per channel
                    throw new NotImplementedException();
                    break;
                case 20000:
                    //sample rate = 1 MHz, 1 s of data = 1M samples per channel
                    //0.1s worth of data

                    ldv_time = new double[fft_pts];
                    for (int i = 0; i < fft_pts; i++)
                    {
                        ldv_time[i] = i * (1e-6);
                    }

                    ldv_time_long = new double[fft_pts * long_mult];
                    for (int i = 0; i < fft_pts * long_mult; i++)
                    {
                        ldv_time_long[i] = i * (1e-6);
                    }

                    ldv_time_old = new double[fft_pts];
                    for (int i = 0; i < fft_pts; i++)
                    {
                        ldv_time_old[i] = (i - fft_pts) * 1e-6;
                    }

                    ldv_fft_data = new double[2][];
                    ldv_fft_data[0] = new double[fft_pts];
                    ldv_fft_data[1] = new double[fft_pts];

                    ldv_fft_data_old = new double[2][];
                    ldv_fft_data_old[0] = new double[fft_pts];
                    ldv_fft_data_old[1] = new double[fft_pts];

                    ldv_fft_data_long = new double[2][];
                    ldv_fft_data_long[0] = new double[fft_pts * long_mult];
                    ldv_fft_data_long[1] = new double[fft_pts * long_mult];

                    fftd_data = new double[fft_pts + 2];
                    fftd_data_old = new double[fft_pts + 2];

                    //frequencies 0Hz to 500000Hz but note cutoff filter at 20000Hz. Frequency is index*10
                    fftd_frequencies = new double[(fft_pts + 2) / 2];
                    for (int f = 0; f < fftd_frequencies.Length; f++)
                    {
                        fftd_frequencies[f] = f * fft_df;
                    }
                    fftd_amplitudes = new double[(fft_pts + 2) / 2];

                    break;
                case 50000:
                    //sample rate = 2 MHz, 1 s of data = 2M sample per channel
                    throw new NotImplementedException();
                    break;
                default:
                    throw new ArgumentOutOfRangeException("configureFFTBuffers: frequency range is not supported " + Properties.Settings.Default.LDVFreqRange.ToString());
            }

            //set up FFT handles
            if (destroyHandles)
            {
                fftw.destroy_plan(fftPlan);
                fft_in.Free();
                fft_out.Free();
            }
            fft_in = GCHandle.Alloc(ldv_fft_data[0], GCHandleType.Pinned); //must be freed later
            fft_out = GCHandle.Alloc(fftd_data, GCHandleType.Pinned); //must be freed later
            fftPlan = fftw.dft_r2c_1d(ldv_fft_data[0].Length, fft_in.AddrOfPinnedObject(), fft_out.AddrOfPinnedObject(), fftw_flags.Estimate);
            destroyHandles = true;
        }

        public void AnalyzeFFT() //get a data packet from LDV then FFT transform and analyze amplitudes to find thermal resonance of probe, plot the resulting transform
        {
            if (!destroyHandles)
            {
                throw new ApplicationException("analyzeFFT: FDController has not been configured properly by configureFFTBuffers");
            }

            //signalDetected = false;
            //probeFree = false;
            //checkProbeTouching();

            ldvInterface.GetDataOnce(ldv_fft_data, AnalyzeFFTPlot);
        }

        private void AnalyzeFFTPlot()
        {
            fftw.execute(fftPlan); //do FFT transform from ldv_fft_data to fftd_data
            for (int f = 0; f < fftd_amplitudes.Length; f++)
            {
                fftd_amplitudes[f] = Math.Sqrt(fftd_data[f * 2] * fftd_data[f * 2] + fftd_data[f * 2 + 1] * fftd_data[f * 2 + 1]) * 2.0 / (fftd_data.Length - 2); //amplitude of FFT (element 0 and n need to be not multiplied by 2 for accurate amplitudes due to folding of positive and negative spectrum here)
            }

            XyDataSeries<double, double> lineData = new XyDataSeries<double, double>();
            lineData.Append(fftd_frequencies, fftd_amplitudes);

            sciChartSurface.Dispatcher.Invoke(() =>
            {
                LineSeriesFFT.DataSeries = lineData;
                //chartSurface.ZoomExtents();
                FDTabs.SelectedIndex = 0;//make sure the plot's tab is visible
            });
        }

        public void AnalyzeFD() //get a data packet from LDV then FFT transform and analyze amplitudes to find thermal resonance of probe, plot the resulting transform
        {
            //signalDetected = false;
            //probeFree = false;
            //checkProbeTouching();

            ldvInterface.GetDataOnce(ldv_fft_data_long, AnalyzeFDPlot);
            Thread.Sleep(600);//wait for LDV to start
            dbInterface.setPiezoStatic(10); //withdraw piezo
            
        }

        private void AnalyzeFDPlot()
        {
            dbInterface.setPiezoStatic(0); //return piezo

            XyDataSeries<double, double> lineData = new XyDataSeries<double, double>();
            lineData.Append(ldv_time_long, ldv_fft_data_long[0]);

            sciChartSurface.Dispatcher.Invoke(() =>
            {
                LineSeriesFD.DataSeries = lineData;
                //chartSurface.ZoomExtents();
                FDTabs.SelectedIndex = 1;//make sure the plot's tab is visible

                //HPAFMLogger.WriteFDPoints3(ldv_fft_data_old[0]); //write data to file

            });

            double min = 0; //find global minimum and quit
            int n = ldv_fft_data_long[0].Count();
            int i = 0;
            for (int p = 0; p < n; p++)
            {
                if (ldv_fft_data_long[0][p] < min)
                {
                    min = ldv_fft_data_long[0][p];
                    i = p;
                }
            }

            XyDataSeries<double, double> pointData = new XyDataSeries<double, double>();
            pointData.AcceptsUnsortedData = true;

            pointData.Append(ldv_time_long[i], ldv_fft_data_long[0][i]); //highlight this end point

            sciChartSurface.Dispatcher.Invoke(() =>
            {
                PointSeriesFD.DataSeries = pointData;
                //chartSurface.ZoomExtents();
                MinText.Text = i.ToString() + "," + min.ToString();
            });

            
        }

        public void AnalyzeFD_State() //use current and previous FFT data to find and measure ping signal, then plot it
        {
            XyDataSeries<double, double> lineData = new XyDataSeries<double, double>();
            //lineData.Append(ldv_time_old, ldv_fft_data_old[0]);
            //lineData.Append(ldv_time, ldv_fft_data[0]);
            lineData.Append(ldv_time_long, ldv_fft_data_long[0]);

            sciChartSurface.Dispatcher.Invoke(() =>
            {
                LineSeriesFD.DataSeries = lineData;
                //chartSurface.ZoomExtents();
                FDTabs.SelectedIndex = 1;//make sure the plot's tab is visible

                //HPAFMLogger.WriteFDPoints3(ldv_fft_data_old[0]); //write data to file
                
            });

            HPAFMLogger.WriteFDPoints4(min_fd_result);
            min_fd_result = 0;
            min_fd_counter = 0;

            double min = 0; //find global minimum and quit
            int n = ldv_fft_data_old[0].Count();
            for (int p=0; p<n; p++)
            {
                if (ldv_fft_data_old[0][p] < min)
                    min = ldv_fft_data_old[0][p];
                if (ldv_fft_data[0][p] < min)
                    min = ldv_fft_data[0][p];
            }

            FDanalysisSuccessful = true;
            
            return;

            FDanalysisSuccessful = false;
            ping_time = new double[10];
            ping_amp = new double[10];

            
            double thr = Properties.Settings.Default.FDTrigLevel; //height of moved line

            while (ldv_fft_data_old[0][n] < thr && n > 0)
            {
                n--;
            }

            if (n == 0)
                return; //unsuccessful analysis

            XyDataSeries<double, double> pointData = new XyDataSeries<double, double>();
            pointData.AcceptsUnsortedData = true;

            int ping_end = n;
            pointData.Append(ldv_time[n], ldv_fft_data_old[0][n]); //highlight this end point
            PointSeriesFD.DataSeries = pointData; //plot for now

            int width = 100; //if threshold not crossed regularly every 100 points (~ sample rate / resonant frequency) assume the ping has ended
            int w = 0; //measured width of not crossing threshold

            while (w < width && n > 0)
            {
                n--;
                if (ldv_fft_data_old[0][n] > thr)
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
            pointData.Append(ldv_time[n], ldv_fft_data_old[0][n]);//highlight this start point
            int n0, n1;
            double mex;

            while (ldv_fft_data_old[0][n] > -thr && n < ping_end)
            {
                n++;
            } //find first downward slope
            n0 = n;
            if (n == ping_end)
                return; //unsuccessful analysis

            for (int p = 0; p < 5; p++) //find 5 first extreme points in a latching amplifier approach (between crossing of -thr and +thr) - if this can be done then it is safe to assume this is a real ping (can be further checked by periodicity between extrema)
            {
                while (ldv_fft_data_old[0][n] < thr && n < ping_end)
                {
                    n++;
                } //find next upward slope
                n1 = n;
                if (n == ping_end)
                    return; //unsuccessful analysis
                mex = 0;
                for (int mn = n0; mn < n1; mn++) //find minimum between n0 and n1
                {
                    if (ldv_fft_data_old[0][mn] < mex)
                    {
                        mex = ldv_fft_data_old[0][mn];
                        n0 = mn;
                    }
                }
                pointData.Append(ldv_time[n0], ldv_fft_data_old[0][n0]); //highlight minimum point
                ping_time[p * 2] = ldv_time[n0];
                ping_amp[p * 2] = ldv_fft_data_old[0][n0];

                while (ldv_fft_data_old[0][n] > -thr && n < ping_end)
                {
                    n++;
                } //find next downward slope
                n0 = n;
                if (n == ping_end)
                    return; //unsuccessful analysis

                mex = 0;
                for (int mn = n1; mn < n0; mn++) //find maximum between n1 and n0
                {
                    if (ldv_fft_data_old[0][mn] > mex)
                    {
                        mex = ldv_fft_data_old[0][mn];
                        n1 = mn;
                    }
                }

                pointData.Append(ldv_time[n1], ldv_fft_data_old[0][n1]); //highlight maximum point
                ping_time[p * 2 + 1] = ldv_time[n1];
                ping_amp[p * 2 + 1] = ldv_fft_data_old[0][n1];
            }

            //LDVOutText.Text = "Analysis Finished!";

            FDanalysisSuccessful = true;

            HPAFMLogger.WriteFDPoints2(ping_time, ping_amp);

            sciChartSurface.Dispatcher.Invoke(() =>
            {
                PointSeriesFD.DataSeries = pointData;
                //chartSurface.ZoomExtents();
            });
        }

        /*private void AnalyzeFDPlot()
        {
            XyDataSeries<double, double> lineData = new XyDataSeries<double, double>();
            lineData.Append(fd_time, ldv_fd_data[1]);

            sciChartSurface.Dispatcher.Invoke(() =>
            {
                LineSeriesFD.DataSeries = lineData;
                //chartSurface.ZoomExtents();
                FDTabs.SelectedIndex = 1;//make sure the plot's tab is visible
            });

            dbInterface.setPiezoStatic(0); //return piezo to origin

            FDanalysisSuccessful = false;
            ping_time = new double[10];
            ping_amp = new double[10];

            int n = ldv_fd_data[1].Count() - 1;
            double thr = Properties.Settings.Default.FDTrigLevel; //height of moved line

            while (ldv_fd_data[1][n] < thr && n > 0)
            {
                n--;
            }

            if (n == 0)
                return; //unsuccessful analysis

            XyDataSeries<double, double> pointData = new XyDataSeries<double, double>();
            pointData.AcceptsUnsortedData = true;

            int ping_end = n;
            pointData.Append(fd_time[n], ldv_fd_data[1][n]); //highlight this end point
            PointSeriesFD.DataSeries = pointData; //plot for now

            int width = 100; //if threshold not crossed regularly every 100 points (~ sample rate / resonant frequency) assume the ping has ended
            int w = 0; //measured width of not crossing threshold

            while (w < width && n > 0)
            {
                n--;
                if (ldv_fd_data[1][n] > thr)
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
            pointData.Append(fd_time[n], ldv_fd_data[1][n]);//highlight this start point
            int n0, n1;
            double mex;

            while (ldv_fd_data[1][n] > -thr && n < ping_end)
            {
                n++;
            } //find first downward slope
            n0 = n;
            if (n == ping_end)
                return; //unsuccessful analysis

            for (int p = 0; p < 5; p++) //find 5 first extreme points in a latching amplifier approach (between crossing of -thr and +thr) - if this can be done then it is safe to assume this is a real ping (can be further checked by periodicity between extrema)
            {
                while (ldv_fd_data[1][n] < thr && n < ping_end)
                {
                    n++;
                } //find next upward slope
                n1 = n;
                if (n == ping_end)
                    return; //unsuccessful analysis
                mex = 0;
                for (int mn = n0; mn < n1; mn++) //find minimum between n0 and n1
                {
                    if (ldv_fd_data[1][mn] < mex)
                    {
                        mex = ldv_fd_data[1][mn];
                        n0 = mn;
                    }
                }
                pointData.Append(fd_time[n0], ldv_fd_data[1][n0]); //highlight minimum point
                ping_time[p * 2] = fd_time[n0];
                ping_amp[p * 2] = ldv_fd_data[1][n0];

                while (ldv_fd_data[1][n] > -thr && n < ping_end)
                {
                    n++;
                } //find next downward slope
                n0 = n;
                if (n == ping_end)
                    return; //unsuccessful analysis

                mex = 0;
                for (int mn = n1; mn < n0; mn++) //find maximum between n1 and n0
                {
                    if (ldv_fd_data[1][mn] > mex)
                    {
                        mex = ldv_fd_data[1][mn];
                        n1 = mn;
                    }
                }

                pointData.Append(fd_time[n1], ldv_fd_data[1][n1]); //highlight maximum point
                ping_time[p * 2 + 1] = fd_time[n1];
                ping_amp[p * 2 + 1] = ldv_fd_data[1][n1];
            }

            //LDVOutText.Text = "Analysis Finished!";

            FDanalysisSuccessful = true;

            sciChartSurface.Dispatcher.Invoke(() =>
            {
                PointSeriesFD.DataSeries = pointData;
                //chartSurface.ZoomExtents();
            });
        }*/

        public void SetPiezoStatic(double volts)
        {
            dbInterface.setPiezoStatic((float)(volts));
            //dbInterface.setkHzwave(false);
        }

        private void NoiseFreqBox_DragEnded(object sender, EventArgs e)
        {
            Properties.Settings.Default.FFTNoiseFreq = ((double)NoiseFreqBox.X1 + (double)NoiseFreqBox.X2) / 2;
            Properties.Settings.Default.FFTNoiseLevel = (double)NoiseFreqBox.Y2;
            NoiseText.Text = "Noise = " + Properties.Settings.Default.FFTNoiseFreq.ToString("00000") + " Hz " + Properties.Settings.Default.FFTNoiseLevel.ToString("0.0e0") + " mm/s";
        }

        private void ResonantFreqBox_DragEnded(object sender, EventArgs e)
        {
            Properties.Settings.Default.FFTProbeFreq = ((double)ResonantFreqBox.X1 + (double)ResonantFreqBox.X2) / 2;
            Properties.Settings.Default.FFTProbeLevel = (double)ResonantFreqBox.Y2;
            ResonantText.Text = "Resonant = " + Properties.Settings.Default.FFTProbeFreq.ToString("00000") + " Hz " + Properties.Settings.Default.FFTProbeLevel.ToString("0.0e0") + " mm/s";
        }

        public ProbeStatus CheckProbeTouching()
        {
            if (!destroyHandles)
            {
                throw new ApplicationException("analyzeFFT: FDController has not been configured properly by configureFFTBuffers or setFFTNums");
            }

            ldvInterface.GetDataOnceBlocking(ldv_fft_data); //wait for data to be available, blocking caller until it is ready
            AnalyzeFFTPlot(); //get amplitudes and plot for visual confirmation

            return CheckProbeTouchingFFT();
        }

        private ProbeStatus CheckProbeTouchingFFT()
        {
            int fft_pscanmin = (int)Math.Round((Properties.Settings.Default.FFTProbeFreq - 500) / fft_df);
            int fft_pscanmax = (int)Math.Round((Properties.Settings.Default.FFTProbeFreq + 500) / fft_df);
            int fft_nscanmin = (int)Math.Round((Properties.Settings.Default.FFTNoiseFreq - 500) / fft_df);
            int fft_nscanmax = (int)Math.Round((Properties.Settings.Default.FFTNoiseFreq + 500) / fft_df);

            //check amplitudes of interest
            double bgpeak = 0;
            for (int f = fft_nscanmin; f < fft_nscanmax; f++)
            {
                if (fftd_amplitudes[f] > bgpeak)
                    bgpeak = fftd_amplitudes[f];//find max
            }

            double prpeak = 0;
            for (int f = fft_pscanmin; f < fft_pscanmax; f++)
            {
                if (fftd_amplitudes[f] > prpeak)
                    prpeak = fftd_amplitudes[f];//find max
            }

            if (bgpeak < Properties.Settings.Default.FFTNoiseLevel)
            {
                if (prpeak > Properties.Settings.Default.FFTProbeLevel)
                {
                    return ProbeStatus.ProbeFree;
                }
                else
                {
                    return ProbeStatus.ProbeTouching;
                }
            }
            else
            {
                return ProbeStatus.NoSignal;
            }
        }

        private void LDVRSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.LDVVelRange = double.Parse((string)((ComboBoxItem)LDVRSelect.SelectedItem).Tag);

            ConfigureLDVRange();
        }

        private void GetFFT_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeFFT();
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Operator request Get FFT");
            }catch(Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "GetFFT_Click error: " + x.Message, true);
            }
        }

        private void GetVel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeFD();
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Operator request Get FD");
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "GetVel_Click error: " + x.Message, true);
            }
        }

        private void OutputVel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HPAFMLogger.WriteFDPoints3(ldv_fft_data_long[0]);
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Operator request Output FD");
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "GetVel_Click error: " + x.Message, true);
            }
        }


        private void FDTrigSelect_DragEnded(object sender, EventArgs e)
        {
            Properties.Settings.Default.FDTrigLevel = (double)FDTrigSelect.Y1;

            FDTrigText.Text = "FD Level = " + Properties.Settings.Default.FDTrigLevel.ToString("0.0e0") + " mm/s";

            //AnalyzeFDPlot();
        }

        public void StartLDVLive(LiveDataReceived ldr)
        {
            liveDataReceived = ldr;
            ldvInterface.GetDataContinuous(ldv_fft_data, LDVLiveDataReceived);
        }

        private void LDVLiveDataReceived()
        {
            AnalyzeFFTPlot();
            ProbeStatus ps = CheckProbeTouchingFFT();

            liveDataReceived(ps);
        }

        public void StopLDVLive()
        {
            ldvInterface.StopDataContinuous();
        }

        public void CopyFFTToBuffer()
        {
            Buffer.BlockCopy(fftd_data, 0, fftd_data_old, 0, fftd_data.Length * 8);
            Buffer.BlockCopy(ldv_fft_data[0], 0, ldv_fft_data_old[0], 0, ldv_fft_data[0].Length * 8);
            if(min_fd_counter < long_mult)
            {
                Buffer.BlockCopy(ldv_fft_data[0], 0, ldv_fft_data_long[0], fft_pts * min_fd_counter * 8, fft_pts * 8);
                min_fd_counter++;
            }
            double min = 0; //find global minimum and quit
            int n = ldv_fft_data[0].Count();
            for (int p = 0; p < n; p++)
            {
                if (ldv_fft_data[0][p] < min)
                    min = ldv_fft_data[0][p];
            }
            if (min < min_fd_result)
                min_fd_result = min;
            //Array.Copy(fftd_data, fftd_data_old, fftd_data.Length);
            //Array.Copy(ldv_fft_data, ldv_fft_data_old, ldv_fft_data[0].Length*2);
        }
    }
}
