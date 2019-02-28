using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
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

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for AutomationController.xaml
    /// </summary>
    public partial class ControlAutomation : Page
    {
        CancellationTokenSource automationCancel; //shared by all background tasks to prevent more than one running at a time
        
        ControlMotors motorController;
        ControlEnvironment envController;
        ControlFD fdController;

        private enum AutomationLDVState { Idle, ApproachMotor, ApproachPiezo, GetFD };
        private enum AutomationLDVResult { None, ErrorNoTouch, ErrorNoRelease, ErrorSignalLoss, FDSuccess };
        // data handling interrupt gets called with LDV data every 10 ms = 100 Hz
        // if state = idle, don't do anything
        // if state = ApproachMotor, slow down rate to ~5Hz using a counter by idling during 19/20 counts, then check FFT of data whether probe is touching and if not then move the motor closer
        // if state = ApproachPiezo, check FFT of data for probe touching and if not then wait (assuming piezo voltage has already been set by controller), if waiting too long then move motor and retract piezo, if probe is touching then send retract command to piezo and switch state to MeasureFD
        // if state = MeasureFD, check FFT of data for probe touching and store a backup copy of data for later use in analysis in case it is found to have the ping signal. If probe is free then use the data along with previous stored run and perform FD analysis on them. Switch to idle state
        private AutomationLDVState MachineState;
        private AutomationLDVResult MachineResult;
        private int MachineCounter;

        private void StartLDVStateMachine()
        {
            //check machine is not null and not running

            //Todo: make the below thread-safe (volatile?)
            MachineState = AutomationLDVState.Idle;
            MachineCounter = 0;
            MachineResult = AutomationLDVResult.None;
            //this will begin calling of ProcessLDVStateMachine() at 100 Hz with LDV data
            fdController.StartLDVLive(ProcessLDVStateMachine);
        }

        private void StopLDVStateMachine()
        {
            //stop calling process function
            fdController.StopLDVLive();
        }

        private void ProcessLDVStateMachine(ControlFD.ProbeStatus ps)
        {
            switch (MachineState)
            {
                case AutomationLDVState.Idle:
                    return;
                case AutomationLDVState.ApproachPiezo:
                    if (ps == ControlFD.ProbeStatus.NoSignal)
                    {
                        MachineState = AutomationLDVState.Idle;
                        MachineResult = AutomationLDVResult.ErrorSignalLoss;
                    }
                    if (ps == ControlFD.ProbeStatus.ProbeTouching)
                    {
                        motorController.SetProbeTouchingLimit();
                        fdController.SetPiezoStatic(10); //begin to fully retract
                        MachineState = AutomationLDVState.GetFD; //and measure starting now
                        MachineCounter = 0;
                    }
                    if (ps == ControlFD.ProbeStatus.ProbeFree)
                    {
                        MachineCounter++;
                        if (MachineCounter > 30)
                        {//increment counter? if waiting too long, return to idle without doing FD
                            MachineState = AutomationLDVState.Idle;
                            MachineResult = AutomationLDVResult.ErrorNoTouch;
                        }
                    }
                    break;
                case AutomationLDVState.ApproachMotor:
                    MachineCounter++;//increment counter? only continue to below on 1/2 counts to wait for motor to stabilize
                    if (MachineCounter % 2 != 0)
                        break;
                    /*if(MachineStateCounter > 10000)
                    {
                        MachineState = AutomationLDVState.Idle;
                        MachineResult = AutomationLDVResult.ErrorNoTouch;
                    }*/

                    /*if (ps == ControlFD.ProbeStatus.NoSignal)
                    {
                        MachineState = AutomationLDVState.Idle;
                        MachineResult = AutomationLDVResult.ErrorSignalLoss;
                    }*/
                    
                    if (ps == ControlFD.ProbeStatus.ProbeTouching || ps == ControlFD.ProbeStatus.NoSignal)
                    {
                        if (MachineCounter > 0)
                        {
                            HPAFMLogger.WriteFDHeader(motorController.CurrentSampleBasis, motorController.CurrentProbeBasis, envController.WaterPressure, envController.WaterTemperature, 0);
                            motorController.SetProbeTouchingLimit();
                            motorController.ProbeApproachInc(-0.0001);
                            MachineCounter = -3;
                        }
                        else
                        {
                            fdController.SetPiezoStatic(10); //begin to fully retract
                            MachineState = AutomationLDVState.GetFD; //and measure starting now
                            MachineCounter = 0;
                        }
                    }
                    if (ps == ControlFD.ProbeStatus.ProbeFree)
                    {
                        motorController.ProbeApproachInc(-0.0001); //approach slowly
                    }
                    break;
                case AutomationLDVState.GetFD:
                    //analyze FFT
                    fdController.CopyFFTToBuffer();//copy FFT data into memory for later
                    MachineCounter++;//No signal is expected during retract
                    if(MachineCounter > 10)
                    {
                        fdController.AnalyzeFD_State(); //FD is analyzed later using LDVdata and LDVdataPrevious combined, ping signal is somewhere in there
                        MachineState = AutomationLDVState.Idle; //avoid overwriting stored LDV data until the parent function can do analysis
                        MachineResult = AutomationLDVResult.FDSuccess;
                    }
                    /*
                    if (ps == ControlFD.ProbeStatus.NoSignal)
                    {
                        fdController.CopyFFTToBuffer();//copy FFT data into memory for later
                        MachineCounter++;//No signal is expected during retract
                        if (MachineCounter > 30)
                        {//increment counter? if waiting too long, return to idle without doing FD
                            MachineState = AutomationLDVState.Idle;
                            MachineResult = AutomationLDVResult.ErrorSignalLoss;
                        }
                    }  
                    if (ps == ControlFD.ProbeStatus.ProbeTouching)
                    {
                        fdController.CopyFFTToBuffer();//copy FFT data into memory for later
                        MachineCounter++;
                        if (MachineCounter > 30)
                        {//increment counter? if waiting too long, return to idle without doing FD
                            MachineState = AutomationLDVState.Idle;
                            MachineResult = AutomationLDVResult.ErrorNoRelease;
                        }
                    }
                    if (ps == ControlFD.ProbeStatus.ProbeFree)
                    {
                        fdController.AnalyzeFD_State(); //FD is analyzed later using LDVdata and LDVdataPrevious combined, ping signal is somewhere in there
                        MachineState = AutomationLDVState.Idle; //avoid overwriting stored LDV data until the parent function can do analysis
                        MachineResult = AutomationLDVResult.FDSuccess;
                    }*/
                    break;
            }
        }

        private void UseLDVStateMachine()
        {
            
        }

        public ControlAutomation(ControlMotors mController, ControlEnvironment eController, ControlFD fController)
        {
            motorController = mController;
            envController = eController;
            fdController = fController;

            InitializeComponent();
        }

        public void StartAutomation(Queue<HPAFMAction> script)
        {
            if (automationCancel != null)
                throw new InvalidOperationException("runAutomation_bg: worker is already running");

            automationCancel = new CancellationTokenSource();

            motorController.ProbeRetractFromSample(); //return to original retracted position
            Task.Run(() => DoAutomation(script, automationCancel.Token));

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Starting automation script");
        }

        public void CancelAutomation()
        {
            if (automationCancel == null)
                throw new InvalidOperationException("cancelAutomation: automation is inactive, nothing to cancel");

            automationCancel.Cancel(); //object set to null in completed handler

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Canceling automation by operator input");
        }

        private void DoAutomation(Queue<HPAFMAction> script, CancellationToken cancel)
        {
            //withdraw probe to limit
            //check machine is not running now
            StartLDVStateMachine();
            
            try
            {
                while (script.Count > 0)
                {
                    HPAFMAction currentAction = script.Dequeue();
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "automationBgWork: starting action " + currentAction.ToString());
                    AutomationStatusText.Dispatcher.Invoke(() => AutomationStatusText.Text = "Current action: " + currentAction.ToString() + "\r\nRemaining actions: " + script.Count.ToString());
                    
                    switch (currentAction.actionType)
                    {
                        case HPAFMAction.Action.FDcurve:

                            
                            fdController.SetPiezoStatic(0); //retract so it is closest to sample and can later be moved away
                                                            //Thread.Sleep(500); //wait for wave to stabilize

                            motorController.ProbeApproachToSample(0.002); //give some room for auto approach to do fine tuning
                            /*
                            for (int i = 0; i < currentAction.arg3; i++)
                            {
                                AutoApproach(automationCancel.Token, false, true);

                                fdController.FDanalysisSuccessful = false;
                                //fdController.GetFDBlocking(); //measure and plot resulting data
                                motorController.ProbeRetractInc(0.002);
                                Thread.Sleep(2000); //wait for piezo to return to original position for repeat trials

                                if (fdController.FDanalysisSuccessful)
                                {
                                    //AutomationStatusText.Text = "ApproachSlow_Click got data:\r\n" + fdController.FDamplitudes[0].ToString();
                                }
                                else
                                {
                                    //AutomationStatusText.Text = "ApproachSlow_Click did not get data";
                                }
                            }
                            */

                            MachineCounter = 0;
                            MachineResult = AutomationLDVResult.None;
                            MachineState = AutomationLDVState.ApproachMotor;
                            while (MachineState != AutomationLDVState.Idle && !cancel.IsCancellationRequested)
                            {
                                Thread.Sleep(100); //wait until done
                            }
                            MachineState = AutomationLDVState.Idle; //in case of cancellation avoid fighting the machine
                            if (MachineResult == AutomationLDVResult.None)
                                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Motor approach no result");
                            if (MachineResult == AutomationLDVResult.ErrorNoTouch)
                                //throw new Exception("Motor approach did not touch.");
                                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Motor approach did not touch");
                            if (MachineResult == AutomationLDVResult.ErrorSignalLoss)
                                //throw new Exception("Motor approach/retract quit due to LDV signal loss.");
                                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Motor approach/retract quit due to LDV signal loss.");
                            if (MachineResult == AutomationLDVResult.ErrorNoRelease)
                                //throw new Exception("Piezo retraction did not release probe.");
                                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Piezo retraction did not release probe.");
                            if (MachineResult == AutomationLDVResult.FDSuccess)
                            {
                                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Motor approach FD test successful!");
                            }

                            ///retract for next FD curve
                            fdController.SetPiezoStatic(0);

                            HPAFMLogger.WriteFDFooter();

                            //call FD curve routine
                            //fdController.MeasureFD(3, 2, currentAction.arg3);

                            //then withdraw to retract_distance so sample can be moved again
                            motorController.ProbeRetractFromSample();
                            Thread.Sleep(500);

                            break;
                        case HPAFMAction.Action.PiezoCal:
                            //assume already withdrawn at retract_distance

                            //approach a few um so actuator is forward-loaded similar to FD setup
                            motorController.ProbeApproachInc(-0.010);
                            Thread.Sleep(200);
                            motorController.ProbeApproachInc();
                            Thread.Sleep(200);

                            //call PC routine
                            //fdController.MeasurePC(1, 2, currentAction.arg3);

                            //withdraw to starting point
                            motorController.ProbeRetractFromSample();

                            break;
                        case HPAFMAction.Action.PTSet:
                            //check T,P setpoints don't cause boiling
                            //convert T into duty cycle (feed-forward)
                            //update heater setpoint (will take a while to propagate effect)
                            //check if new pressure setpoint is compatible with current temperature (ie don't drop pressure->boil)
                            //if not, wait until temperature drops adequately
                            //update pressure setpoint
                            //wait here until P,T are reached
                            envController.SetTarget(currentAction.arg2, currentAction.arg1);
                            Thread.Sleep(2000); //some important work
                            break;
                        case HPAFMAction.Action.SampleLoc:
                            //assume already withdrawn

                            //go to sample x position, wait until reached
                            double x = currentAction.arg1;
                            motorController.GoToSampleLoc(x);
                            Thread.Sleep(500);
                            
                            /*
                            //approach until probe is touching
                            switch (currentAction.arg3)
                            {
                                case 1: //slow (auto) approach
                                    motorController.ProbeApproachToSample(0.001); //give some room for auto approach to do fine tuning
                                    AutoApproach(cancel, false, true);

                                    //motorController.ProbeApproachToSample(0.020); //get within 20um then let auto approach
                                    //AutoApproach(cancel, false); //this will update lastTouchLoc and set piezo voltage = 0
                                    /*if (fdController.beamQuality)
                                    {
                                        motorController.setProbeTouchingLimit(); //make a new limit 
                                    }
                                    else
                                    {
                                        throw new ApplicationException("Automation AutoApproach: ended with a bad beam quality detected");
                                    }*/
                                    /*break;
                                case 2: //fast (no feedback) approach
                                    motorController.ProbeApproachToSample(0.001); //stop early so we can approach final position using 1um steps
                                    motorController.ProbeApproachInc(); //this should match last probe touching position
                                    break;
                                default:
                                    throw new ApplicationException("Automation SampleLoc: Undefined approach type " + currentAction.arg3.ToString());
                            }

                            //put piezo up volt=retract_voltage, alternatively use fast approach without auto approach
                            //dbInterface.setPiezoStatic(retract_voltage); //now get back to retract_voltage for the following down movements
                            Thread.Sleep(200);

                            //go down more according to down parameter
                            motorController.ProbeApproachInc(-currentAction.arg2);
                            Thread.Sleep(200);*/

                            //assume in the future probe will be withdrawn by FDcurve (enforced by xml parser)
                            break;
                    }

                    if (cancel.IsCancellationRequested)
                    {
                        //withdraw probe to limit

                        break; //cancelled
                    }
                }
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "automationBgWork: could not do automation action: " + x.Message);
            }

            //withdraw probe to limit
            motorController.ProbeRetractFromSample();
            //dbInterface.setPiezoStatic(0); //turn off piezo

            StopLDVStateMachine();

            automationCancel = null; //delete the source here to allow restarting later
        }

        public void AutoApproachFast()
        {
            motorController.ProbeApproachToSample(0.001); //stop early so we can approach final position using 1um steps
            motorController.ProbeApproachInc(); //this should match last probe touching position
        }

        public void StartAutoApproach()
        {
            if (!fdController.FFTReady || !motorController.CheckBasisEstablished)
                throw new InvalidOperationException("autoApproach_bg: cannot do this without FFT scan completion and sample basis setup");

            if (automationCancel != null)
                throw new InvalidOperationException("autoApproach_bg: worker is already running");

            automationCancel = new CancellationTokenSource();

            Task.Run(() => AutoApproach(automationCancel.Token));
            AutomationStatusText.Text = "Auto approach active";
        }

        public void CancelAutoApproach()
        {
            if (automationCancel == null)
                throw new InvalidOperationException("cancelAutoApproach: auto approach is inactive, nothing to cancel");

            automationCancel.Cancel(); //object set to null in completed handler
            //AutomationStatusText.Text = "Auto approach cancelled";
        }

        private void AutoApproach(CancellationToken cancel, bool deleteSource = true, bool extraSlow = false)
        {
            //check machine is not running now
            StartLDVStateMachine();

            ///auto-approach with motor and get one FD curve:
            fdController.SetPiezoStatic(0);
            Thread.Sleep(1000);
            MachineCounter = 0;
            MachineResult = AutomationLDVResult.None;
            MachineState = AutomationLDVState.ApproachMotor;
            while (MachineState != AutomationLDVState.Idle && !cancel.IsCancellationRequested)
            {
                Thread.Sleep(100); //wait until done
            }
            if (MachineResult == AutomationLDVResult.None)
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Motor approach no result");
            if (MachineResult == AutomationLDVResult.ErrorNoTouch)
                //throw new Exception("Motor approach did not touch.");
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Motor approach did not touch");
            if (MachineResult == AutomationLDVResult.ErrorSignalLoss)
                //throw new Exception("Motor approach/retract quit due to LDV signal loss.");
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Motor approach/retract quit due to LDV signal loss.");
            if (MachineResult == AutomationLDVResult.ErrorNoRelease)
                //throw new Exception("Piezo retraction did not release probe.");
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Piezo retraction did not release probe.");
            if (MachineResult == AutomationLDVResult.FDSuccess)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Motor approach FD test successful!");
            }

            ///get another FD curve with piezo approach:
            /*fdController.SetPiezoStatic(0);
            MachineCounter = 0;
            MachineResult = AutomationLDVResult.None;
            MachineState = AutomationLDVState.ApproachPiezo;
            while (MachineState != AutomationLDVState.Idle && !cancel.IsCancellationRequested)
            {
                Thread.Sleep(100); //wait until done
            }
            if (MachineResult == AutomationLDVResult.None)
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Piezo approach no result");
            if (MachineResult == AutomationLDVResult.ErrorNoTouch)
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Piezo approach did not touch");
            if (MachineResult == AutomationLDVResult.ErrorSignalLoss)
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Piezo approach/retract quit due to LDV signal loss.");
            if (MachineResult == AutomationLDVResult.ErrorNoRelease)
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Piezo retraction did not release probe.");
            if (MachineResult == AutomationLDVResult.FDSuccess)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Piezo approach FD test successful!");
            }

            //withdraw probe and move to new location on sample
            //motorController.ProbeRetractFromSample();
            //motorController.GoToSampleLoc(10);
            //use a small retract distance and small sample location delta for fastest/safest approach next time
            */

            fdController.SetPiezoStatic(0); //turn off piezo to exit

            if (deleteSource) //allow keeping the cancellation source if this function is called from within automation
                automationCancel = null;

            StopLDVStateMachine();
            return;

            ControlFD.ProbeStatus ps = fdController.CheckProbeTouching();

            while (ps == ControlFD.ProbeStatus.ProbeFree)
            {
                try
                {
                    if(extraSlow)
                        motorController.ProbeApproachInc(-0.0001);
                    else
                        motorController.ProbeApproachInc();
                }
                catch(Exception x)
                {
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "autoApproachBgWork: cancelling due to motor error " + x.Message);
                    break; //overshoot or other motor error
                }
                //Thread.Sleep(200); //wait to stabilize before checking PLL amplitude next

                if (cancel.IsCancellationRequested)
                {
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Error, "autoApproachBgWork: cancelling by operator input");
                    break; //overshoot or cancelled
                }

                ps = fdController.CheckProbeTouching();

                if (ps == ControlFD.ProbeStatus.NoSignal)
                {
                    ps = fdController.CheckProbeTouching(); //double-check presence of noise
                }
            }

            if (ps == ControlFD.ProbeStatus.NoSignal)
            {
                //throw new ApplicationException("autoApproach_bg: signal lost, quitting");
                //AutomationStatusText.Text = "Auto approach signal lost!";
                //need to get to where a signal is good
            }
            else
            {
                if (ps == ControlFD.ProbeStatus.ProbeTouching)
                {
                    if(motorController.CheckApproachCompleted) //allow first time to be set manually by operator
                        motorController.SetProbeTouchingLimit();
                    //AutomationStatusText.Text = "Auto approach successful";
                }
                else
                {
                    //AutomationStatusText.Text = "Auto approach exited";
                }
            }            
        }

        private void ApproachSlow_Click(object sender, RoutedEventArgs e)
        {
            if (automationCancel != null)
                throw new InvalidOperationException("ApproachSlow_Click: worker is already running");

            automationCancel = new CancellationTokenSource();

            fdController.FDanalysisSuccessful = false;
            fdController.SetPiezoStatic(0); //retract so it is closest to sample and can later be moved away
            //Thread.Sleep(500); //wait for wave to stabilize

            motorController.ProbeApproachToSample(0.001); //give some room for auto approach to do fine tuning
            AutoApproach(automationCancel.Token, false, true);

            //fdController.GetFDBlocking(); //measure and plot resulting data

            motorController.ProbeRetractFromSample(); //return to original retracted position

            if (fdController.FDanalysisSuccessful)
            {
                AutomationStatusText.Text = "ApproachSlow_Click got data:\r\n";// + fdController.ping_amp[0].ToString();
            }
            else
            {
                AutomationStatusText.Text = "ApproachSlow_Click did not get data";
            }          

            automationCancel = null;
        }

        private void ApproachFast_Click(object sender, RoutedEventArgs e)
        {
            if (automationCancel != null)
                throw new InvalidOperationException("ApproachFast_Click: worker is already running");

            automationCancel = new CancellationTokenSource();

            fdController.FDanalysisSuccessful = false;
            fdController.SetPiezoStatic(0); //retract so it is closest to sample and can later be moved away
            //Thread.Sleep(500); //wait for wave to stabilize

            AutoApproachFast();

            //fdController.GetFDBlocking(); //measure and plot resulting data

            motorController.ProbeRetractFromSample(); //return to original retracted position

            if (fdController.FDanalysisSuccessful)
            {
                AutomationStatusText.Text = "ApproachFast_Click got data:\r\n";// + fdController.ping_amp[0].ToString();
            }
            else
            {
                AutomationStatusText.Text = "ApproachFast_Click did not get data";
            }

            automationCancel = null;
        }
    }
}
