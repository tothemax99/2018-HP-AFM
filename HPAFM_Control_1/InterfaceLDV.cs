using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Holobright.LDV.Devices;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Threading;
using FFTWSharp;
using System.Runtime.InteropServices;

namespace HPAFM_Control_1
{
    public class InterfaceLDV
    {
        Device LDVDevice;
        const int ID = 12330;
        CancellationTokenSource continuousDataRead;

        public delegate void DataReady(); //store external handler for calling after work is done

        //Velocity ranges: 0.0001, 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2  =  10um/s/V (+-100um/s max) to 200mm/s/V (+-2m/s max)
        //Displacement ranges: 1E-10, 2E-10, 5E-10, 1E-09, 2E-09, 5E-09, 1E-08, 2E-08, 5E-08, 1E-07, 2E-07, 5E-07,
        //                     1E-06 { 2E-06, 5E-06, 1E-05, 2E-05, 5E-05, 0.0001, 0.0002, 0.0005, 0.001, 0.002, 0.005,
        //                     0.01, 0.02, 0.05, 0.1 }  =  10pm/V (+-100pm max) to 100nm/V (+-1um max) { to 10mm/V (+-100mm max) - only enabled at lowest frequency ranges - check in LDV studio }
        //Frequency ranges: { 10, 20, 50, 100, } 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 250000  Hz
        //Measurement modes: Displacement, Velocity
        //Sample rates (automatically set by freq range): { 2000, 4000, } 10000, 20000, 40000, 100000, 200000, 400000, 1000000, 2000000, 4000000, 10000000 { 20000000 }  Hz
        //Freq range to sample rate mapping: 200 > 10000, 500 > 20000, 1000 > 40000, 2000 > 100000, 5000 > 200000, 10000 > 400000, 20000 > 1000000, 50000 > 2000000, 100000 > 4000000, 200000 > 10000000, 250000 > 10000000
        //Wavelength: 6.328e-7  m
        //OnBoardBufferSize: Size_All
        //OnBoardBufferSizeMax: 327680
        //TransMode: Continuous
        //TriggerMode: Internal
        //TriggerEdge: RisingEdge

        public void InitializeLDV()
        {
            if (LDVDevice != null)
                throw new ApplicationException("LDV cannot be initialized as it is already initialized.");

            try
            {
                LDVDevice = new Device();
            }
            catch (ApplicationException e) //thrown if device is not connected
            {
                throw new ApplicationException("LDV USB connection could not be established.", e);
            }

            if (LDVDevice.ProductNumber != ID)
            {
                LDVDevice = null;
                throw new ApplicationException("LDV product ID does not match expected value.");
            }

            ///Set up all measurement params: frequency range, type=velocity/displacement, velocity/displacement range
            ///then call Configure() to apply new settings immediately
        }

        public void GetDataContinuous(double[][] data, DataReady dataReady)
        {
            if (LDVDevice == null)
                throw new ApplicationException("GetDataContinuous: LDV is not initialized, cannot continue");

            if (continuousDataRead != null)
                throw new InvalidOperationException("GetDataContinuous: task is already running");

            continuousDataRead = new CancellationTokenSource();

            LDVDevice.ReleaseBuffer(); //delete all old data
            LDVDevice.ConfigureBuffer(4 * 8 * data[0].Length);

            Task.Run(() => LdvStart()); //start LDV data transfers
            Task.Run(() => ReadData(data, dataReady, continuousDataRead.Token));
            
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Starting LDV continuous reading");
        }

        public void StopDataContinuous()
        {
            if (continuousDataRead == null)
                throw new InvalidOperationException("StopDataContinuous: reading is inactive, nothing to cancel");

            continuousDataRead.Cancel(); //this will be set to null once task completes

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Stopping LDV continuous read by operator input");
        }

        public void GetDataOnce(double[][] data, DataReady dataReady)
        {
            if (LDVDevice == null)
                throw new ApplicationException("GetDataOnce: LDV is not initialized, cannot continue");

            if (continuousDataRead != null)
                throw new InvalidOperationException("GetDataOnce: task is already running");

            continuousDataRead = new CancellationTokenSource();

            LDVDevice.ReleaseBuffer(); //delete all old data
            LDVDevice.ConfigureBuffer(4 * 8 * data[0].Length);

            Task.Run(() => LdvStart()); //start LDV data transfers
            Task.Run(() => ReadData(data, dataReady, continuousDataRead.Token, true));
        }

        public void GetDataOnceBlocking(double[][] data)
        {
            if (LDVDevice == null)
                throw new ApplicationException("GetDataOnce: LDV is not initialized, cannot continue");

            if (continuousDataRead != null)
                throw new InvalidOperationException("GetDataOnce: task is already running");

            continuousDataRead = new CancellationTokenSource();

            LDVDevice.ReleaseBuffer(); //delete all old data
            LDVDevice.ConfigureBuffer(4 * 8 * data[0].Length);

            Task.Run(() => LdvStart()); //start LDV data transfers
            while (!LDVDevice.IsRunning) { Thread.Sleep(10); }
            LDVDevice.ReadMultiChannel(data, data[0].Length);
            LDVDevice.Stop(); //stop here to prevent buffer overrun

            continuousDataRead = null;
        }

        private void ReadData(double[][] dat, DataReady dataReady, CancellationToken ct, bool once = false)
        {
            // Check running flag use to multi thread.
            while (!LDVDevice.IsRunning) { Thread.Sleep(10); }

            // Read measurement data
            try
            {
                if (once)
                {
                    LDVDevice.ReadMultiChannel(dat, dat[0].Length);
                    LDVDevice.Stop(); //stop here to prevent buffer overrun
                    dataReady();
                }
                else
                {
                    while (!ct.IsCancellationRequested) //run indefinitely until cancelled
                    {
                        LDVDevice.ReadMultiChannel(dat, dat[0].Length);
                        dataReady();
                    }
                    LDVDevice.Stop();
                }
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "ReadData in LDV: encountered error: " + x.Message);
            }

            continuousDataRead = null; //delete the source here to allow restarting later
        }

        private void LdvStart()
        {
            try
            {
                LDVDevice.Start();
            }catch(Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "LDVStart: error encountered: " + x.Message, true);
            }
        }

        public void Exit()
        {
            if (LDVDevice == null)
                return;

            LDVDevice.Stop();

            LDVDevice.ReleaseBuffer();

            LDVDevice.Dispose();

            LDVDevice = null;
        }

        public void ConfigureVelocityMode(double velRange)
        {
            if (LDVDevice == null)
                throw new ApplicationException("ConfigureVelocityMode: LDV is not initialized, cannot continue");

            LDVDevice.FrequencyRange = Properties.Settings.Default.LDVFreqRange;
            LDVDevice.MeasurementType = MeasurementTypes.Velocity;
            LDVDevice.VelocityRange = velRange;// 0.5 => 50mm/s/V, 10V=500mm/s
            //LDVDevice.OnBoardBufferSize = OnBoardBufferSize.Size_16K;
            LDVDevice.TransMode = TransferModes.Continuous; //send the data at fast rate
            LDVDevice.Configure();

            //double msr = LDVDevice.MaxSampleRate;
            //int mbs = LDVDevice.OnBoardBufferSizeMax;            
        }

        public void ConfigureDisplacementMode(double dispRange)
        {
            throw new NotImplementedException();
            
            /*if (LDVDevice == null)
                throw new ApplicationException("SetDisplacementMode: LDV is not initialized, cannot continue");

            LDVDevice.FrequencyRange = 2000;
            LDVDevice.MeasurementType = MeasurementTypes.Displacement;
            LDVDevice.RemoveDCOffset = RemoveDCOffsets.Enable;
            LDVDevice.DisplacementRange = 2e-5;// 2um/V, 10V=20um
            LDVDevice.HighPassFilterEnable = HighPassFilter.Disable;
            LDVDevice.Configure();*/
            //above is tested configuration for 1Hz differential displacement measurements
            //LDVDevice.Configure(); //call this again in Displacement mode to remove DC offset
        }
    }
}
