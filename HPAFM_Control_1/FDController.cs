using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel;
using SciChart.Charting.Visuals;
using System.Runtime.InteropServices;
using FFTWSharp;
using SciChart.Charting.Model.DataSeries;
using System.Windows.Threading;

namespace HPAFM_Control_1
{
    class FDController
    {
        InterfaceDataBox dbInterface;
        InterfacePIMotors piInterface;
        InterfaceLDV ldvInterface;
        SciChartSurface chartSurface;

        double probe_range = -3; //3mm from probe zero to maximum allowed
        const double probe_approach_step = 0.001; //step by 1um = 0.001mm
        const double probe_retract = 0.01; //step by 10um
        public const double sample_range = 29.21; //1.15in = 29.21 mm from sample zero to maximum allowed
        public const double retract_distance = 0.050; //default retract distance from sample in automation mode
        const float retract_voltage = 10.0f; //default retract piezo voltage in automation mode (set below 10V to reduce piezo thermal depolarization)

        bool piBoundsSet = false;
        double sampleZeroLoc, probeZeroLoc; //these are in motor coordinates 0-52mm
        double currentSampleLoc, currentProbeLoc; //these are in sample coordinates 0-29.21 and 0-(-5) mm respectively
        //current motor position = sampleZeroLoc + currentSampleLoc or probeZeroLoc + currentProbeLoc

        public double CurrentSampleLoc { get { return currentSampleLoc; } }
        public double CurrentProbeLoc { get { return currentProbeLoc; } }

        bool autoApproachDone = false;
        bool signalDetected = false, probeFree = false;
        double lastTouchLoc; //this is in sample coordinates 0-5 mm
        RunWorkerCompletedEventHandler autoApproachDone_external, automationDone_external; //store external handler for calling after work is done

        double[][] ldv_fd_data; //data store for LDV 1 second, 2 channels
        double[][] ldv_fft_data; //data store for LDV 0.1 second, 2 channels
        double[] fd_time; //time to plot above data
        double[] fftd_data; //data after FFT has been called, complex format [re][im]
        double[] fftd_amplitudes; //amplitude only of FFT data
        double[] fftd_frequencies; //frequencies corresponding to amplitudes

        //values to scan FFT for auto-approach and contact detection, in the same units as index of fftd_amplitudes array (here 10 Hz and mm/s)
        int fft_pscanmin=0, fft_pscanmax; //probe FFT frequencies
        double fft_pscanlevel, fft_nscanlevel; //FFT amplitude level above which the probe is considered free or beam is considered too noisy
        int fft_nscanmin, fft_nscanmax; //noise FFT frequencies

        GCHandle fft_in, fft_out; //when reserving memory for FFT, ensure it will be unreserved to avoid memory eating
        IntPtr fftPlan;
        bool destroyHandles = false;

        bool isEngaged = false; //blocks sample motion if engaged

        BackgroundWorker bgMotionWorker = null; //shared by auto-approach and fd and piezo-cal measurements, ensures only one can be done at a time

        public FDController(InterfaceDataBox idb, InterfacePIMotors ipi, InterfaceLDV ildv, SciChartSurface chart)
        {
            //All are pre-initialized by caller
            dbInterface = idb;
            piInterface = ipi;
            ldvInterface = ildv;
            chartSurface = chart;

            //FFT wrapper and example: Github tszalay/FFTWSharp

            //Importing wisdom (wisdom speeds up the plan creation process, if that plan was previously created at least once)
            //fftwf.import_wisdom_from_filename("wisdom.wsd");

            //ldvInterface.SetVelocityMode();
        }

        ~FDController()
        {
            if (destroyHandles)
            {
                //fftw.export_wisdom_to_filename("wisdom.wsd");
                fftw.destroy_plan(fftPlan);
                fft_in.Free();
                fft_out.Free();
            }
        }

        public void moveSampleAxisInc(double inc)
        {
            if (isEngaged)
                throw new ApplicationException("moveSampleAxisInc: cannot move while engaged");

            if (piBoundsSet)
            {
                double nv = currentSampleLoc + inc;
                if (nv < 0 || nv > sample_range)
                    throw new ApplicationException("moveSampleAxisInc: motion would be outside sample basis range, setpoint=" + nv.ToString());
                currentSampleLoc = nv;
                piInterface.MoveMotorAbs(2, sampleZeroLoc + currentSampleLoc, true);
            }
            else
            {
                piInterface.MoveMotorInc(2, inc, true);
            }
        }

        public void moveProbeAxisInc(double inc)
        {
            if (isEngaged)
                throw new ApplicationException("moveProbeAxisInc: cannot move while engaged");

            if (piBoundsSet)
            {
                double nv = currentProbeLoc + inc;
                if (nv > 0 || nv < probe_range)
                    throw new ApplicationException("moveProbeAxisInc: motion would be outside sample basis range, setpoint=" + nv.ToString());
                currentProbeLoc = nv;
                piInterface.MoveMotorAbs(1, probeZeroLoc + currentProbeLoc, true);
            }
            else
            {
                piInterface.MoveMotorInc(1, inc, true);
            }
        }

        public void setPIMotorSampleBasis() //this assumes current motor positions are the boundaries:
        {//farthest back (lowest value) of probe, and farthest left (lowest value) of sample
            //if (piBoundsSet) //only set once
            //    return false;

            try
            {
                probeZeroLoc = piInterface.GetMotorPosition(1); //this is the point to which probe should be retracted and beyond which the probe cannot be moved
                sampleZeroLoc = piInterface.GetMotorPosition(2); //the leftmost point of sample, point beyond which sample motor cannot move

                piInterface.SetMotorLimits(1, probeZeroLoc + probe_range - 0.05, probeZeroLoc + 0.05); //set soft limits on motion
                piInterface.SetMotorLimits(2, sampleZeroLoc - 0.05, sampleZeroLoc + sample_range + 0.05);
            }catch(Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "setPIMotorSampleBasis: unable to complete operation: " + x.Message);
                return;
            }

            currentProbeLoc = 0;
            currentSampleLoc = 0;

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Sample basis origin established at probe=" + probeZeroLoc.ToString() + ", sample=" + sampleZeroLoc.ToString());

            piBoundsSet = true;
        }
        

        public void autoApproach_bg(ProgressChangedEventHandler approachProgress, RunWorkerCompletedEventHandler approachDone)
        {
            if (fft_pscanmin == 0 || !piBoundsSet)
                throw new InvalidOperationException("autoApproach_bg: cannot do this without FFT scan completion and sample basis setup");

            if (bgMotionWorker != null)
                throw new InvalidOperationException("autoApproach_bg: worker is already running");

            autoApproachDone_external = approachDone;

            bgMotionWorker = new BackgroundWorker();
            bgMotionWorker.WorkerReportsProgress = true;
            bgMotionWorker.WorkerSupportsCancellation = true;
            bgMotionWorker.RunWorkerCompleted += autoApproachBgCompleted;
            bgMotionWorker.ProgressChanged += approachProgress;
            bgMotionWorker.DoWork += autoApproachBgWork;
            bgMotionWorker.RunWorkerAsync();

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Starting auto approach");
        }

        public void cancelAutoApproachBg()
        {
            if (bgMotionWorker == null)
                throw new InvalidOperationException("cancelAutoApproach: auto approach is inactive, nothing to cancel");

            bgMotionWorker.CancelAsync(); //object set to null in completed handler

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Canceling auto approach by operator input");
        }

        private void autoApproachBgWork(object sender, DoWorkEventArgs e)
        {
            e.Result = false;

            try
            {
                dbInterface.setPiezoStatic(0); //retract so it is closest to sample and can later be moved away
                Thread.Sleep(500); //wait for wave to stabilize
                BackgroundWorker me = (BackgroundWorker)sender;

                checkProbeTouching();

                while (signalDetected && probeFree)
                {
                    currentProbeLoc -= probe_approach_step;
                    piInterface.MoveMotorAbs(1, probeZeroLoc + currentProbeLoc, false);
                    Thread.Sleep(200); //wait to stabilize before checking PLL amplitude next

                    if(currentProbeLoc < probe_range)
                    {
                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "autoApproachBgWork: probe limit exceeded, canceling auto approach with z=" + (probeZeroLoc + currentProbeLoc).ToString());
                        e.Cancel = true;
                        return; //overshoot or cancelled
                    }

                    if (me.CancellationPending)
                    {
                        e.Cancel = true;
                        return; //overshoot or cancelled
                    }

                    me.ReportProgress(1, probeZeroLoc + currentProbeLoc);
                    checkProbeTouching();
                }

                me.ReportProgress(0, probeZeroLoc + currentProbeLoc); //a final progress report with ending position and ampl
            }
            catch(Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "autoApproachBgWork: could not do auto approach: " + x.Message);
                return;
            }

            if (!signalDetected)
            {
                throw new ApplicationException("autoApproach_bg: signal lost, quitting at z=" + (probeZeroLoc + currentProbeLoc).ToString());
                //need to get to where a signal is good
            }

            dbInterface.setPiezoStatic(10); //move away from sample and check probe is free again
            Thread.Sleep(500); //wait for piezo to stabilize
            checkProbeTouching();

            if (signalDetected && probeFree)
            {
                lastTouchLoc = currentProbeLoc;
                autoApproachDone = true;
                isEngaged = true;
            }

            e.Result = true;
        }

        private void autoApproachBgCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //BackgroundWorker me = (BackgroundWorker)sender;
            //me.ReportProgress(dbInterface.getPLLampl(), currentProbeLoc);
            //Cannot report progress once it is completed
            bgMotionWorker = null; //reset for next time
            if (e.Cancelled)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Auto approach canceled, probe location is " + (probeZeroLoc + currentProbeLoc) + "mm, PLL_ampl = " + dbInterface.getPLLampl());
                return;
            }
            if(e.Error != null)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "Auto approach encountered error: " + e.Error.Message);
                return;
            }
            if ((bool)e.Result == true)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Auto approach successful, probe location is " + (probeZeroLoc + currentProbeLoc) + "mm, PLL_ampl = " + dbInterface.getPLLampl());
                autoApproachDone_external(sender, e); //call external handler on success
            }
            else
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Auto approach unsuccessful, probe location is " + (probeZeroLoc + currentProbeLoc) + "mm, PLL_ampl = " + dbInterface.getPLLampl());
            }
            
        }

        public void setApproachLimit() //uses current probe position as a maximum so cannot crash too badly
        {
            if (!piBoundsSet || !isEngaged || !autoApproachDone)
                throw new ApplicationException("setApproachLimit: not engaged or no sample basis range established");

            if (currentProbeLoc > -0.3) //give ability to withdraw up to 300um from sample, if probeZeroLoc is too close to sample move it back
            {
                double dd = 0.3 + currentProbeLoc;
                probeZeroLoc += dd;
                currentProbeLoc -= dd;
                lastTouchLoc = currentProbeLoc;
            }

            probe_range = currentProbeLoc - 0.02; //give a 20um possible forward motion

            piInterface.SetMotorLimits(1, probeZeroLoc + probe_range - 0.05, probeZeroLoc + 0.05); //update soft limits on motion
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Probe range limit adjusted to " + probe_range.ToString());
        }

        public void goToSampleLoc(double sampleloc)
        {
            if (!piBoundsSet || isEngaged)
                throw new ApplicationException("goToSampleLoc: cannot move while engaged or without sample basis range established");

            if (sampleloc < 0 || sampleloc > sample_range)
                throw new ArgumentOutOfRangeException("goToSampleLoc: motion would be outside sample basis range, setpoint=" + sampleloc.ToString());

            currentSampleLoc = sampleloc;
            piInterface.MoveMotorAbs(2, sampleZeroLoc + currentSampleLoc, true);
        }

        public void probeRetractInc(double inc=0.010)
        {//retract by minimum 10um such that next approach finishes with a forward-loaded actuator, moving by smaller increments can create 'play' in the actuator
            if (!piBoundsSet)
                throw new InvalidOperationException("probeRetractInc: sample basis has to be set");

            if (inc < 0.010 || inc > 0.1)
                throw new ArgumentOutOfRangeException("probeRetractInc: increment is out of range, inc=" + inc.ToString());

            currentProbeLoc += inc;
            if (currentProbeLoc > 0)
            {
                currentProbeLoc = 0;
                //return;
            }

            piInterface.MoveMotorAbs(1, probeZeroLoc + currentProbeLoc, true);
            isEngaged = false;
        }

        public void probeRetractFromSample(double sampleOffset = 0.010)
        {//retract by minimum 10um such that next approach finishes with a forward-loaded actuator, moving by smaller increments can create 'play' in the actuator
            if (!piBoundsSet || !autoApproachDone)
                throw new InvalidOperationException("probeRetractFromSample: sample basis has to be set and autoapproach done");

            if (sampleOffset < 0.010 || sampleOffset > 0.1)
                throw new ArgumentOutOfRangeException("probeRetractFromSample: offset is out of range, sampleOffset=" + sampleOffset.ToString());

            currentProbeLoc = lastTouchLoc + sampleOffset;
            if (currentProbeLoc > 0)
            {
                currentProbeLoc = 0;
            }

            piInterface.MoveMotorAbs(1, probeZeroLoc + currentProbeLoc, true);
            isEngaged = false;
        }

        public void probeRetractFull()
        {
            if (!piBoundsSet)
                throw new InvalidOperationException("probeRetractFull: sample basis has to be set");

            currentProbeLoc = 0;
            piInterface.MoveMotorAbs(1, probeZeroLoc + currentProbeLoc, true);
            isEngaged = false;
        }

        public void configureLDVAnalysis()
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
                    ldv_fd_data = new double[2][];
                    ldv_fd_data[0] = new double[1000000];
                    ldv_fd_data[1] = new double[1000000];
                    fd_time = new double[1000000];
                    for (int i = 0; i < 1000000; i++)
                    {
                        fd_time[i] = i * (1e-6);
                    }

                    //0.1s worth of data
                    ldv_fft_data = new double[2][];
                    ldv_fft_data[0] = new double[100000];
                    ldv_fft_data[1] = new double[100000];
                    fftd_data = new double[100002];

                    //frequencies 0Hz to 500000Hz but note cutoff filter at 20000Hz. Frequency is index*10
                    fftd_frequencies = new double[50001];
                    for (int f = 0; f < 50001; f++)
                    {
                        fftd_frequencies[f] = f * 10;
                    }
                    fftd_amplitudes = new double[50001];

                    break;
                case 50000:
                    //sample rate = 2 MHz, 1 s of data = 2M sample per channel
                    throw new NotImplementedException();
                    break;
                default:
                    throw new ArgumentOutOfRangeException("configureLDV: frequency range is not supported " + Properties.Settings.Default.LDVFreqRange.ToString());
            }

            //set up FFT handles
            if (destroyHandles)
            {
                fftw.destroy_plan(fftPlan);
                fft_in.Free();
                fft_out.Free();
            }
            fft_in = GCHandle.Alloc(ldv_fft_data[1], GCHandleType.Pinned); //must be freed later
            fft_out = GCHandle.Alloc(fftd_data, GCHandleType.Pinned); //must be freed later
            fftPlan = fftw.dft_r2c_1d(ldv_fft_data[1].Length, fft_in.AddrOfPinnedObject(), fft_out.AddrOfPinnedObject(), fftw_flags.Estimate);
            destroyHandles = true;
        }

        public void analyzeFFT() //get a data packet from LDV then FFT transform and analyze amplitudes to find thermal resonance of probe, plot the resulting transform
        {
            if(!destroyHandles)
            {
                throw new ApplicationException("analyzeFFT: FDController has not been configured properly by configureLDVAnalysis");
            }

            //signalDetected = false;
            //probeFree = false;
            //checkProbeTouching();

            ldvInterface.GetDataOnce(ldv_fft_data, analyzeFFTDone);
        }

        private void analyzeFFTDone()
        {
            fftw.execute(fftPlan); //do FFT transform from ldv_fft_data to fftd_data
            for (int f = 0; f < fftd_amplitudes.Length; f++)
            {
                fftd_amplitudes[f] = Math.Sqrt(fftd_data[f * 2] * fftd_data[f * 2] + fftd_data[f * 2 + 1] * fftd_data[f * 2 + 1]) * 2.0 / (fftd_data.Length - 2); //amplitude of FFT (element 0 and n need to be not multiplied by 2 for accurate amplitudes due to folding of positive and negative spectrum here)
            }

            XyDataSeries<double, double> lineData = new XyDataSeries<double, double>();
            lineData.Append(fftd_frequencies, fftd_amplitudes);

            chartSurface.Dispatcher.Invoke(() =>
            {
                chartSurface.RenderableSeries[0].DataSeries = lineData;
                //chartSurface.ZoomExtents();
            });
        }

        public void analyzeFD() //get FD data packet and display it to check noise level
        {
            dbInterface.setPiezoStatic(0);// set to full approach
            Thread.Sleep(1000); //wait to get there
            dbInterface.setPiezoStatic(10); //set to full retract
            ldvInterface.GetDataOnce(ldv_fd_data, analyzeFDDone);//while the piezo is retracting get 1 second of data, if there's a ping it will be in this set
        }

        private void analyzeFDDone()
        {
            XyDataSeries<double, double> lineData = new XyDataSeries<double, double>();
            lineData.Append(fd_time, ldv_fd_data[1]);

            chartSurface.Dispatcher.Invoke(() =>
            {
                chartSurface.RenderableSeries[0].DataSeries = lineData;
                //chartSurface.ZoomExtents();
            });
        }

        public void autoApproach()
        {
            dbInterface.setPiezoStatic(0); //retract so it is closest to sample and can later be moved away
            Thread.Sleep(500); //wait for piezo to stabilize

            checkProbeTouching();

            while (signalDetected && probeFree)
            {
                currentProbeLoc -= probe_approach_step;
                piInterface.MoveMotorAbs(1, probeZeroLoc + currentProbeLoc, false);
                Thread.Sleep(200); //wait to stabilize before checking PLL amplitude next

                if (currentProbeLoc < probe_range)
                {
                    //overshoot or cancelled
                    throw new ApplicationException("autoApproach: probe range limits exceeded, quitting at z=" + (probeZeroLoc + currentProbeLoc).ToString());
                }

                checkProbeTouching();
            }

            if (!signalDetected)
            {
                throw new ApplicationException("autoApproach: signal lost, quitting at z=" + (probeZeroLoc + currentProbeLoc).ToString());
                //need to get to where a signal is good
            }

            dbInterface.setPiezoStatic(10); //move away from sample and check probe is free again
            Thread.Sleep(500); //wait for piezo to stabilize
            checkProbeTouching();

            if (signalDetected && probeFree)
            {
                lastTouchLoc = currentProbeLoc;
                autoApproachDone = true;
                isEngaged = true;
            }
        }

        public bool checkProbeFFT(double probeFreq, double probeMinLevel, double noiseFreq, double noiseMaxLevel)
        {// units are Hz and mm/s
            if (probeFreq < 1000 || probeFreq > 20000 || probeMinLevel < 1e-5 || probeMinLevel > 1e-2)
                throw new ArgumentOutOfRangeException("setProbeFFT: probeFreq/level is out of range " + probeFreq.ToString() + probeMinLevel.ToString());

            if (noiseFreq < 1000 || noiseFreq > 20000 || noiseMaxLevel < 1e-5 || noiseMaxLevel > 1e-2)
                throw new ArgumentOutOfRangeException("setProbeFFT: noiseFreq/level is out of range " + noiseFreq.ToString() + noiseMaxLevel.ToString());

            if(Math.Abs(probeFreq - noiseFreq) < 2000)
                throw new ArgumentOutOfRangeException("setProbeFFT: noiseFreq " + noiseFreq.ToString() + " is too close to probeFreq " + probeFreq.ToString());

            fft_pscanmin = (int)Math.Round((probeFreq - 500) / 10);
            fft_pscanmax = (int)Math.Round((probeFreq + 500) / 10);
            fft_nscanmin = (int)Math.Round((noiseFreq - 500) / 10);
            fft_nscanmax = (int)Math.Round((noiseFreq + 500) / 10);

            fft_pscanlevel = probeMinLevel;
            fft_nscanlevel = noiseMaxLevel;

            checkProbeTouching();

            return signalDetected && probeFree;
        }

        private void checkProbeTouching()
        {
            if (!destroyHandles || fft_pscanmin == 0)
            {
                throw new ApplicationException("analyzeFFT: FDController has not been configured properly by configureLDVAnalysis");
            }

            ldvInterface.GetDataOnceBlocking(ldv_fft_data); //wait for data to be available, blocking caller until it is ready

            fftw.execute(fftPlan); //do FFT transform from ldv_fft_data to fftd_data
            
            //check amplitudes of interest
            double bgpeak = 0;
            for (int f = fft_nscanmin; f < fft_nscanmax; f++)
            {
                fftd_amplitudes[f] = Math.Sqrt(fftd_data[f * 2] * fftd_data[f * 2] + fftd_data[f * 2 + 1] * fftd_data[f * 2 + 1]) * 2.0 / (fftd_data.Length - 2); //amplitude of FFT (element 0 and n need to be not multiplied by 2 for accurate amplitudes due to folding of positive and negative spectrum here)
                if (fftd_amplitudes[f] > bgpeak)
                    bgpeak = fftd_amplitudes[f];//find max
            }

            double prpeak = 0;
            for (int f = fft_pscanmin; f < fft_pscanmax; f++)
            {
                fftd_amplitudes[f] = Math.Sqrt(fftd_data[f * 2] * fftd_data[f * 2] + fftd_data[f * 2 + 1] * fftd_data[f * 2 + 1]) * 2.0 / (fftd_data.Length - 2); //amplitude of FFT (element 0 and n need to be not multiplied by 2 for accurate amplitudes due to folding of positive and negative spectrum here)
                if (fftd_amplitudes[f] > prpeak)
                    prpeak = fftd_amplitudes[f];//find max
            }

            if (bgpeak < fft_nscanlevel)
            {
                signalDetected = true;
                if (prpeak > fft_pscanlevel)
                {
                    probeFree = true;
                }
                else
                {
                    probeFree = false;
                }
            }
            else
            {
                signalDetected = false;
                //probeFree = false;
            }
        }


        public void probeApproachToSample(double sampleOffset=0.001)
        {//quickly approaches sample using last touch position
            if (isEngaged || !autoApproachDone)
                throw new InvalidOperationException("probeApproachFast: cannot do while engaged or doing other tasks");

            if (sampleOffset < 0.001 || sampleOffset > 0.1)
                throw new ArgumentOutOfRangeException("probeApproachFast: sample offset is out of range, sampleOffset=" + sampleOffset.ToString());

            currentProbeLoc = lastTouchLoc + sampleOffset;
            piInterface.MoveMotorAbs(1, probeZeroLoc + currentProbeLoc, true);

            isEngaged = true;
        }

        public void probeApproachInc(double inc=-0.001)
        {
            if (!piBoundsSet)
                throw new InvalidOperationException("probeApproachSlow: sample basis has to be set");

            if(inc > 0 || inc < -0.010)
            {
                throw new ArgumentOutOfRangeException("probeApproachSlow: increment is out of range, inc=" + inc.ToString());
            }

            currentProbeLoc += inc;
            piInterface.MoveMotorAbs(1, probeZeroLoc + currentProbeLoc, true);
        }

        public void measureFD(double pressure_rec, double temperature_rec, int num)
        {
            float vstart = 10, vmid = 0;

            HPAFMLogger.WriteFDHeader(currentSampleLoc, currentProbeLoc, pressure_rec, temperature_rec, num);
            
            for (int i = 0; i < num; i++)
            {
                try
                {
                    ldvInterface.GetDataOnceBlocking(ldv_fd_data);
                    //List<InterfaceDataBox.FDPoint> points = dbInterface.doFD(vstart, vmid);
                    //ldvInterface.GetDataAsync(ldv_fd_data);
                    //analyze fd data to find ping and amplitude, convert velocity magnitude to displacement magnitude
                    //HPAFMLogger.WriteFDPoints(points);
                    //HPAFMLogger.AnalyzeFDPoints(points);
                }
                catch (Exception x)
                {
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "measureFD: could not complete FD measurement: " + x.Message);
                }
            }
            HPAFMLogger.WriteFDFooter();
        }

        public void measurePC(double pressure_rec, double temperature_rec, int num)
        {
            float vstart = 10, vmid = 0;

            HPAFMLogger.WritePiezoCalHeader(pressure_rec, temperature_rec, num);

            for (int i = 0; i < num; i++)
            {
                //List<InterfaceDataBox.PiezoCalPoint> points = dbInterface.doPiezoCal(vstart, vmid);
                //ldvInterface.GetDataAsync(ldv_fd_data);
                //integrate LDV velocities to get displacement over time
                //HPAFMLogger.WritePiezoCalPoints(points);
            }

            HPAFMLogger.WritePiezoCalFooter();
        }

        public void runAutomation_bg(Queue<HPAFMAction> script, ProgressChangedEventHandler automationProgress, RunWorkerCompletedEventHandler automationDone)
        {
            if(bgMotionWorker != null)
                throw new InvalidOperationException("runAutomation_bg: worker is already running");

            automationDone_external = automationDone;

            bgMotionWorker = new BackgroundWorker();
            bgMotionWorker.WorkerReportsProgress = true;
            bgMotionWorker.WorkerSupportsCancellation = true;
            bgMotionWorker.RunWorkerCompleted += automationBgCompleted;
            bgMotionWorker.ProgressChanged += automationProgress;
            bgMotionWorker.DoWork += automationBgWork;
            bgMotionWorker.RunWorkerAsync(script);

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Starting automation script");
        }

        public void cancelAutomationBg()
        {
            if (bgMotionWorker == null)
                throw new InvalidOperationException("cancelAutomation: automation is inactive, nothing to cancel");

            bgMotionWorker.CancelAsync(); //object set to null in completed handler

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Canceling automation by operator input");
        }

        private void automationBgWork(object sender, DoWorkEventArgs e)
        {
            e.Result = false;

            Queue<HPAFMAction> script = (Queue<HPAFMAction>)e.Argument;
            BackgroundWorker me = (BackgroundWorker)sender;

            //withdraw probe to limit

            try
            {
                while(script.Count > 0)
                {
                    HPAFMAction currentAction = script.Dequeue();
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "automationBgWork: starting action " + currentAction.ToString());
                    me.ReportProgress(script.Count, currentAction);
                    switch (currentAction.actionType)
                    {
                        case HPAFMAction.Action.FDcurve:
                            //assume already engaged by SampleLoc (enforced by xml parser)

                            //call FD curve routine
                            measureFD(3, 2, currentAction.arg3);

                            //then withdraw to retract_distance so sample can be moved again
                            probeRetractFromSample(retract_distance);

                            break;
                        case HPAFMAction.Action.PiezoCal:
                            //assume already withdrawn at retract_distance
                            
                            //approach a few um so actuator is forward-loaded similar to FD setup
                            probeApproachInc(-0.010);
                            Thread.Sleep(200);
                            probeApproachInc();
                            Thread.Sleep(200);

                            //call PC routine
                            measurePC(1, 2, currentAction.arg3);

                            //withdraw to starting point
                            probeRetractFromSample(retract_distance);

                            break;
                        case HPAFMAction.Action.PTSet:
                            //check T,P setpoints don't cause boiling
                            //convert T into duty cycle (feed-forward)
                            //update heater setpoint (will take a while to propagate effect)
                            //check if new pressure setpoint is compatible with current temperature (ie don't drop pressure->boil)
                            //if not, wait until temperature drops adequately
                            //update pressure setpoint
                            //wait here until P,T are reached
                            Thread.Sleep(2000); //some important work
                            break;
                        case HPAFMAction.Action.SampleLoc:
                            //assume already withdrawn

                            //go to sample x position, wait until reached
                            double x = currentAction.arg1;
                            goToSampleLoc(x);
                            Thread.Sleep(200);

                            //approach until probe is touching
                            switch (currentAction.arg3)
                            {
                                case 1: //slow (auto) approach
                                    probeApproachToSample(0.020); //get within 20um then let auto approach
                                    autoApproach(); //this will update lastTouchLoc and set piezo voltage = 0
                                    int ampl_touch = dbInterface.getPLLampl();
                                    if (ampl_touch > 5000 && ampl_touch < 7000)
                                    {
                                        setApproachLimit(); //make a new limit 
                                    }
                                    else
                                    {
                                        throw new ApplicationException("Automation AutoApproach: ended with a bad oscillation amplitude value: " + ampl_touch.ToString());
                                    }
                                    break;
                                case 2: //fast (no feedback) approach
                                    probeApproachToSample(0.001); //stop early so we can approach final position using 1um steps
                                    probeApproachInc(); //this should match last probe touching position
                                    break;
                                default:
                                    throw new ApplicationException("Automation SampleLoc: Undefined approach type " + currentAction.arg3.ToString());
                            }

                            //put piezo up volt=retract_voltage, alternatively use fast approach without auto approach
                            dbInterface.setPiezoStatic(retract_voltage); //now get back to retract_voltage for the following down movements
                            Thread.Sleep(200);

                            //go down more according to down parameter
                            probeApproachInc(-currentAction.arg2);
                            Thread.Sleep(200);

                            //assume in the future probe will be withdrawn by FDcurve (enforced by xml parser)
                            break;
                    }

                    if (me.CancellationPending)
                    {
                        //withdraw probe to limit
                        e.Result = false;
                        e.Cancel = true;
                        break; //cancelled
                    }
                }
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "automationBgWork: could not do automation action: " + x.Message);
                return;
            }

            //withdraw probe to limit
            probeRetractFromSample(retract_distance);
            dbInterface.setPiezoStatic(0); //turn off piezo

            e.Result = true;
        }

        private void automationBgCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //HPAFMLogger.WritePiezoCalFooter();
            bgMotionWorker = null; //reset for more calls
            if (e.Cancelled)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Automation has been canceled");
                return;
            }
            if (e.Error != null)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "Automation has quit with an error: " + e.Error.Message);
                return;
            }
            if ((bool)e.Result == true)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Automation has completed successfully");
                automationDone_external(sender, e); //call external handler on success
            }
            else
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Automation has quit without successful completion");
            }
        }
    }

    public struct HPAFMAction
    {
        public enum Action { FDcurve, SampleLoc, PiezoCal, PTSet };
        public Action actionType;
        public double arg1, arg2;
        public int arg3;
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            switch (actionType)
            {
                case Action.FDcurve:
                    sb.Append("[FD Curve (vstart, vmid, num)][");
                    sb.Append(arg1);
                    sb.Append(',');
                    sb.Append(arg2);
                    sb.Append(',');
                    sb.Append(arg3);
                    sb.Append(']');
                    break;
                case Action.PiezoCal:
                    sb.Append("[PiezoCal (vstart, vmid, num)][");
                    sb.Append(arg1);
                    sb.Append(',');
                    sb.Append(arg2);
                    sb.Append(',');
                    sb.Append(arg3);
                    sb.Append(']');
                    break;
                case Action.PTSet:
                    sb.Append("[PTSet (P, T)][");
                    sb.Append(arg1);
                    sb.Append(',');
                    sb.Append(arg2);
                    sb.Append(']');
                    break;
                case Action.SampleLoc:
                    sb.Append("[SampleLoc (x, down, approach)][");
                    sb.Append(arg1);
                    sb.Append(',');
                    sb.Append(arg2);
                    sb.Append(',');
                    switch (arg3)
                    {
                        case 1:
                            sb.Append("slow");
                            break;
                        case 2:
                            sb.Append("fast");
                            break;
                        default:
                            sb.Append("undefined");
                            break;
                    }
                    sb.Append(']');
                    break;
            }
            return sb.ToString();
        }
    }
}
