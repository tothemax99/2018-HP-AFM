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

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for MotorsController.xaml
    /// </summary>
    public partial class ControlMotors : Page
    {
        InterfacePIMotors piMotorsInterface;
        InterfaceThorMotorInertial tiInterface;

        const double probe_approach_step = 0.001; //step by 1um = 0.001mm
        const double probe_retract = 0.01; //step by 10um
        const double retract_distance = 0.050; //default retract distance from sample in automation mode
        const float retract_voltage = 10.0f; //default retract piezo voltage in automation mode (set below 10V to reduce piezo thermal depolarization)
        const double limit_padding = 0.001; //ensure double comparisons remain valid

        double stepSizeMm = 0.01; //step size for motor alignment step

        bool probeBasisEstablished = false, sampleBasisEstablished = false; //limits motor motion within range of sample
        double sampleBasisZero, probeBasisZero; //these are in motor coordinates 0-52mm
        double currentSampleBasis, currentProbeBasis; //these are in sample coordinates 0-29.21 and 0-(-5) mm respectively
        public double CurrentSampleBasis { get { return currentSampleBasis; } }
        public double CurrentProbeBasis { get { return currentProbeBasis; } }
        //current motor position = sampleZeroLoc + currentSampleLoc or probeZeroLoc + currentProbeLoc
        const double probe_range = 3.5; //3.5mm from probe zero to maximum allowed
        const double probe_sample_drive = 0.02; //20um forward motion into sample allowed
        public const double sample_range = 29.21; //1.15in = 29.21 mm from sample zero to maximum allowed
        const string disp_format = "00.000000"; //format for motor position
        double lastTouchLoc = 0; //in probe basis
        bool probeTouchEstablished = false; //allows relative motion to sample location, must be set by calling setProbeTouchingLimit

        private bool BlockMotionEngaged = false;

        public bool CheckBasisEstablished { get { return probeBasisEstablished && sampleBasisEstablished; } }
        public bool CheckApproachCompleted { get { return probeTouchEstablished; } }

        public ControlMotors(InterfacePIMotors pii, InterfaceThorMotorInertial iti)
        {
            piMotorsInterface = pii;//assume already initialized
            tiInterface = iti;

            InitializeComponent();

            if (piMotorsInterface.IsMotorHomed(1))
            {
                ProbeAxisText.Text = piMotorsInterface.GetMotorPosition(1).ToString(disp_format);
                ProbeHome.Visibility = Visibility.Collapsed;
                ProbeAxisText.Visibility = Visibility.Visible;

                ProbeAxisM.IsEnabled = true; //allow movement
                ProbeAxisP.IsEnabled = true;
                ProbeBasisZero.IsEnabled = true;

                if (Properties.Settings.Default.LastProbeBasis > 0)
                { //there is a previously stored probe basis and motor is homed, so restore it
                    try
                    {
                        probeBasisZero = Properties.Settings.Default.LastProbeBasis;
                        double currentPos = piMotorsInterface.GetMotorPosition(1); //this is the point to which probe should be retracted and beyond which the probe cannot be moved
                        piMotorsInterface.SetMotorLimits(1, probeBasisZero - limit_padding, probeBasisZero + probe_range + limit_padding); //set soft limits on motion
                        currentProbeBasis = currentPos - probeBasisZero;
                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Probe basis origin recalled from last time at Z=" + probeBasisZero.ToString());
                        ProbeBasisText.Text = currentProbeBasis.ToString(disp_format);
                        ProbeBasisZero.Visibility = Visibility.Collapsed;
                        ProbeBasisText.Visibility = Visibility.Visible;
                        probeBasisEstablished = true;
                    }
                    catch (Exception x)
                    {
                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "ControlMotors: unable to recall last probe basis: " + x.Message);
                        probeBasisEstablished = false;
                        Properties.Settings.Default.LastProbeBasis = -1;
                    }
                }
            }
            else
            {
                probeBasisEstablished = false;
                Properties.Settings.Default.LastProbeBasis = -1;
            }

            if (piMotorsInterface.IsMotorHomed(2))
            {
                SampleAxisText.Text = piMotorsInterface.GetMotorSetpt(2).ToString(disp_format);
                SampleHome.Visibility = Visibility.Collapsed;
                SampleAxisText.Visibility = Visibility.Visible;

                SampleAxisM.IsEnabled = true; //allow movement
                SampleAxisP.IsEnabled = true;
                SampleBasisZero.IsEnabled = true;

                if (Properties.Settings.Default.LastSampleBasis > 0)
                { //there is a previously stored sample basis and motor is homed, so restore it
                    try
                    {
                        sampleBasisZero = Properties.Settings.Default.LastSampleBasis;
                        double currentPos = piMotorsInterface.GetMotorPosition(2); //the leftmost point of sample, point beyond which sample motor cannot move
                        piMotorsInterface.SetMotorLimits(2, sampleBasisZero - limit_padding, sampleBasisZero + sample_range + limit_padding);
                        currentSampleBasis = currentPos - sampleBasisZero;
                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Sample basis origin recalled from last time at X=" + sampleBasisZero.ToString());

                        SampleBasisText.Text = currentSampleBasis.ToString(disp_format);
                        SampleBasisZero.Visibility = Visibility.Collapsed;
                        SampleBasisText.Visibility = Visibility.Visible;
                        sampleBasisEstablished = true;
                    }
                    catch (Exception x)
                    {
                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "ControlMotors: Unable to restore last sample basis:" + x.Message);
                        sampleBasisEstablished = false;
                        Properties.Settings.Default.LastSampleBasis = -1;
                    }
                }
            }
            else
            {
                sampleBasisEstablished = false;
                Properties.Settings.Default.LastSampleBasis = -1;
            }
        }

        private async void ProbeHome_Click(object sender, RoutedEventArgs e)
        {
            ProbeHome.IsEnabled = false;

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "PI Motor 1 homing starting");
            MessageBoxResult hy = MessageBox.Show("PI Motor 1 (probe axis) has not been homed. Perform homing now? Ensure the collar is NOT connected at this point.", "Homing", MessageBoxButton.OKCancel);
            if (hy == MessageBoxResult.OK)
            {
                try
                {
                    await Task.Run(() => piMotorsInterface.HomeMotor(1, InterfacePIMotors.HomingType.Negative, true));
                }
                catch (Exception x)
                {
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Unable to home PI 1 linear motor: " + x.Message, true);
                    return;
                }
            }
            else
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Operator cancelled PI Motor 1 homing.");
                ProbeHome.IsEnabled = true;
                return;
            }

            ProbeAxisText.Text = piMotorsInterface.GetMotorPosition(1).ToString(disp_format);
            ProbeHome.Visibility = Visibility.Collapsed;
            ProbeAxisText.Visibility = Visibility.Visible;

            ProbeAxisM.IsEnabled = true; //allow movement
            ProbeAxisP.IsEnabled = true;
            ProbeBasisZero.IsEnabled = true;

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "PI Motor 1 homing successful");
        }

        private async void SampleHome_Click(object sender, RoutedEventArgs e)
        {
            SampleHome.IsEnabled = false;

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "PI Motor 2 homing starting");
            MessageBoxResult hy = MessageBox.Show("PI Motor 2 (sample axis) has not been homed. Perform homing now? Ensure the collar is NOT connected at this point.", "Homing", MessageBoxButton.OKCancel);
            if (hy == MessageBoxResult.OK)
            {
                try
                {
                    await Task.Run(() => piMotorsInterface.HomeMotor(2, InterfacePIMotors.HomingType.Negative, true));
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
                SampleHome.IsEnabled = true;
                return;
            }

            SampleAxisText.Text = piMotorsInterface.GetMotorSetpt(2).ToString(disp_format);
            SampleHome.Visibility = Visibility.Collapsed;
            SampleAxisText.Visibility = Visibility.Visible;

            SampleAxisM.IsEnabled = true; //allow movement
            SampleAxisP.IsEnabled = true;
            SampleBasisZero.IsEnabled = true;

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "PI Motor 2 homing successful");
        }

        private void SampleBasisZero_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                sampleBasisZero = piMotorsInterface.GetMotorPosition(2); //the leftmost point of sample, point beyond which sample motor cannot move
                piMotorsInterface.SetMotorLimits(2, sampleBasisZero - limit_padding, sampleBasisZero + sample_range + limit_padding);
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "SampleBasisZero_Click: unable to complete operation: " + x.Message, true);
                return;
            }

            currentSampleBasis = 0;
            Properties.Settings.Default.LastSampleBasis = sampleBasisZero;

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Sample basis origin established at X=" + sampleBasisZero.ToString());

            SampleBasisText.Text = currentSampleBasis.ToString(disp_format);
            SampleBasisZero.Visibility = Visibility.Collapsed;
            SampleBasisText.Visibility = Visibility.Visible;
            sampleBasisEstablished = true;
        }

        private void ProbeBasisZero_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                probeBasisZero = piMotorsInterface.GetMotorPosition(1) - probe_range; //this is the point to which probe should be retracted and beyond which the probe cannot be moved
                piMotorsInterface.SetMotorLimits(1, probeBasisZero - limit_padding, probeBasisZero + probe_range + limit_padding); //set soft limits on motion
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "ProbeBasisZero_Click: unable to complete operation: " + x.Message, true);
                return;
            }

            currentProbeBasis = probe_range;
            Properties.Settings.Default.LastProbeBasis = probeBasisZero;

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Probe basis origin established at Z=" + probeBasisZero.ToString());

            ProbeBasisText.Text = currentProbeBasis.ToString(disp_format);
            ProbeBasisZero.Visibility = Visibility.Collapsed;
            ProbeBasisText.Visibility = Visibility.Visible;
            probeBasisEstablished = true;
        }

        private async void MotionButton_Click(object sender, RoutedEventArgs e)
        {
            Button s = (Button)sender;
            string d = (string)s.Tag;
            s.IsEnabled = false;

            try
            {
                if (d[0] == 'X')
                {//x-axis = sample motor = motor 2
                    switch (d[1])
                    {
                        case '+':
                            //motor 2 +
                            await Task.Run(() => MoveSampleAxisInc(stepSizeMm));
                            break;
                        case '-':
                            //motor 2 -
                            await Task.Run(() => MoveSampleAxisInc(-stepSizeMm));
                            break;
                    }
                    SampleAxisText.Text = piMotorsInterface.GetMotorSetpt(2).ToString(disp_format);
                    if (sampleBasisEstablished)
                    {
                        SampleBasisText.Text = currentSampleBasis.ToString(disp_format);
                    }
                }
                else
                {//z-axis = probe motor = motor 1
                    switch (d[1])
                    {
                        case '+':
                            //motor 1 +
                            await Task.Run(() => MoveProbeAxisInc(stepSizeMm));
                            break;
                        case '-':
                            //motor 1 -
                            await Task.Run(() => MoveProbeAxisInc(-stepSizeMm));
                            break;
                    }
                    ProbeAxisText.Text = piMotorsInterface.GetMotorSetpt(1).ToString(disp_format);
                    if (probeBasisEstablished)
                    {
                        ProbeBasisText.Text = currentProbeBasis.ToString(disp_format);
                    }
                }
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "MotionButton_Click: unable to move motor: " + x.Message, true);
            }

            s.IsEnabled = true;
        }

        private void StepSize_Checked(object sender, RoutedEventArgs e)
        {// update step size with checkbox content
            RadioButton rb = (RadioButton)sender;
            if (rb.IsChecked == true)
            {
                stepSizeMm = double.Parse((string)rb.Content);
            }
        }

        public void MoveSampleAxisInc(double inc)
        {
            if (BlockMotionEngaged)
                throw new ApplicationException("moveSampleAxisInc: cannot move while engaged");

            piMotorsInterface.MoveMotorInc(2, inc, true); //this honors basis limits if they have been set
            if (sampleBasisEstablished) //if haven't thrown exception by now the increment is within limits
            {
                currentSampleBasis += inc;
            }
        }

        public void MoveProbeAxisInc(double inc)
        {
            if (BlockMotionEngaged && Math.Abs(inc) > 0.0015)
                throw new ApplicationException("moveProbeAxisInc: cannot move by over 0.0015 mm while engaged");

            piMotorsInterface.MoveMotorInc(1, inc, true);
            if (probeBasisEstablished) //if haven't thrown exception by now the increment is within limits
            {
                currentProbeBasis += inc;
            }
        }

        public void SetProbeTouchingLimit() //uses current probe position as a maximum so cannot crash too badly
        {
            if (!probeBasisEstablished)
                throw new ApplicationException("setApproachLimit: no sample basis range established");

            //adjust zero such that currentProbeBasis = 0.0 and allow 20um possible forward motion into sample
            // z=probeBasisZero + currentProbeBasis = const
            // probeBasisZero = z - 0.0
            // currentProbeBasis = 0.0

            double z = probeBasisZero + currentProbeBasis;
            currentProbeBasis = 0;
            probeBasisZero = z;
            lastTouchLoc = currentProbeBasis;
            probeTouchEstablished = true;

            BlockMotionEngaged = true;

            piMotorsInterface.SetMotorLimits(1, probeBasisZero - limit_padding - probe_sample_drive, probeBasisZero + probe_range + limit_padding); //update soft limits on motion
            ProbeBasisText.Dispatcher.Invoke(() => { ProbeBasisText.Text = currentProbeBasis.ToString(disp_format); });

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Probe touching limit recorded at Z=" + piMotorsInterface.GetMotorSetpt(1).ToString());
        }

        public void GoToSampleLoc(double sampleloc)
        {
            if (!sampleBasisEstablished || BlockMotionEngaged)
                throw new ApplicationException("goToSampleLoc: cannot move while engaged or without sample basis established");

            if (sampleloc < 0 || sampleloc > sample_range)
                throw new ArgumentOutOfRangeException("goToSampleLoc: motion would be outside sample basis range, setpoint=" + sampleloc.ToString());

            piMotorsInterface.MoveMotorAbs(2, sampleBasisZero + sampleloc, true);
            currentSampleBasis = sampleloc;//update if above does not throw exception

            SampleBasisText.Dispatcher.Invoke(() =>
            {
                SampleBasisText.Text = currentSampleBasis.ToString(disp_format);
                SampleAxisText.Text = piMotorsInterface.GetMotorSetpt(2).ToString(disp_format);
            });
        }

        public void ProbeRetractInc(double inc = 0.010)
        {//retract by minimum 10um such that next approach finishes with a forward-loaded actuator, moving by smaller increments can create 'play' in the actuator
            if (!probeBasisEstablished)
                throw new InvalidOperationException("probeRetractInc: probe basis has to be set");

            if (inc < 0 || inc > 0.1)
                throw new ArgumentOutOfRangeException("probeRetractInc: increment is out of range, inc=" + inc.ToString());

            piMotorsInterface.MoveMotorAbs(1, probeBasisZero + currentProbeBasis + inc, true);
            currentProbeBasis += inc;//update if above does not throw exception

            ProbeBasisText.Dispatcher.Invoke(() =>
            {
                ProbeBasisText.Text = currentProbeBasis.ToString(disp_format);
                ProbeAxisText.Text = piMotorsInterface.GetMotorSetpt(1).ToString(disp_format);
            });
        }

        public void ProbeRetractFromSample(double sampleOffset = retract_distance)
        {//retract by minimum 10um such that next approach finishes with a forward-loaded actuator, moving by smaller increments can create 'play' in the actuator
            if (!probeBasisEstablished || !probeTouchEstablished)
                throw new InvalidOperationException("probeRetractFromSample: probe basis has to be set and autoapproach done");

            if (sampleOffset < 0.010 || sampleOffset > 0.1)
                throw new ArgumentOutOfRangeException("probeRetractFromSample: offset is out of range, sampleOffset=" + sampleOffset.ToString());

            piMotorsInterface.MoveMotorAbs(1, probeBasisZero + lastTouchLoc + sampleOffset, true);
            currentProbeBasis = lastTouchLoc + sampleOffset;//update if above does not throw exception

            ProbeBasisText.Dispatcher.Invoke(() =>
            {
                ProbeBasisText.Text = currentProbeBasis.ToString(disp_format);
                ProbeAxisText.Text = piMotorsInterface.GetMotorSetpt(1).ToString(disp_format);
            });
            BlockMotionEngaged = false;
        }

        public void ProbeRetractFull()
        {
            if (!probeBasisEstablished)
                throw new InvalidOperationException("probeRetractFull: probe basis has to be set");

            piMotorsInterface.MoveMotorAbs(1, probeBasisZero, true);
            currentProbeBasis = 0;

            ProbeBasisText.Dispatcher.Invoke(() =>
            {
                ProbeBasisText.Text = currentProbeBasis.ToString(disp_format);
                ProbeAxisText.Text = piMotorsInterface.GetMotorSetpt(1).ToString(disp_format);
            });
            BlockMotionEngaged = false;
        }

        public void ProbeApproachToSample(double sampleOffset = 0.001)
        {//quickly approaches sample using last touch position
            if (BlockMotionEngaged || !probeTouchEstablished)
                throw new InvalidOperationException("probeApproachFast: cannot do while engaged or if touch position not established");

            if (sampleOffset < 0.001 || sampleOffset > 0.1)
                throw new ArgumentOutOfRangeException("probeApproachFast: sample offset is out of range, sampleOffset=" + sampleOffset.ToString());

            piMotorsInterface.MoveMotorAbs(1, probeBasisZero + lastTouchLoc + sampleOffset, true);
            currentProbeBasis = lastTouchLoc + sampleOffset;

            ProbeBasisText.Dispatcher.Invoke(() =>
            {
                ProbeBasisText.Text = currentProbeBasis.ToString(disp_format);
                ProbeAxisText.Text = piMotorsInterface.GetMotorSetpt(1).ToString(disp_format);
            });
            BlockMotionEngaged = true;
        }

        public void ProbeApproachInc(double inc = -probe_approach_step)
        {
            if (!probeBasisEstablished)
                throw new InvalidOperationException("probeApproachSlow: probe basis has to be set");

            if (inc > 0 || inc < -0.010)
            {
                throw new ArgumentOutOfRangeException("probeApproachSlow: increment is out of range, inc=" + inc.ToString());
            }

            piMotorsInterface.MoveMotorAbs(1, probeBasisZero + currentProbeBasis + inc, true);
            currentProbeBasis += inc;

            ProbeBasisText.Dispatcher.Invoke(() =>
            {
                ProbeBasisText.Text = currentProbeBasis.ToString(disp_format);
                ProbeAxisText.Text = piMotorsInterface.GetMotorSetpt(1).ToString(disp_format);
            });
        }

        private async void MirrorButton_Click(object sender, RoutedEventArgs e)
        {//blocking function
            //motors should be set up as 1,2 are horizontal,vertical on mirror 1, 3,4 are horizontal,vertical on mirror 2 (closest to camera)
            Button s = (Button)sender;
            string d = (string)s.Tag;
            s.IsEnabled = false;
            try
            {
                if (d[0] == 'R')
                {//rotate
                    switch (d[1])
                    {
                        case 'U':
                            //motor 4 -
                            await Task.Run(() => tiInterface.MoveMotorInc(4, -Properties.Settings.Default.InertialStepSize));
                            break;
                        case 'D':
                            //motor 4 +
                            await Task.Run(() => tiInterface.MoveMotorInc(4, Properties.Settings.Default.InertialStepSize));
                            break;
                        case 'L':
                            //motor 3 +
                            await Task.Run(() => tiInterface.MoveMotorInc(3, Properties.Settings.Default.InertialStepSize));
                            break;
                        case 'R':
                            //motor 3 -
                            await Task.Run(() => tiInterface.MoveMotorInc(3, -Properties.Settings.Default.InertialStepSize));
                            break;
                    }
                }
                else
                {//shift
                    switch (d[1])
                    {
                        case 'U':
                            //motor 2 -
                            //motor 4 +
                            await Task.Run(() =>
                            {
                                tiInterface.MoveMotorInc(2, -Properties.Settings.Default.InertialStepSize);
                                tiInterface.MoveMotorInc(4, Properties.Settings.Default.InertialStepSize);
                            });
                            break;
                        case 'D':
                            //motor 2 +
                            //motor 4 -
                            await Task.Run(() =>
                            {
                                tiInterface.MoveMotorInc(2, Properties.Settings.Default.InertialStepSize);
                                tiInterface.MoveMotorInc(4, -Properties.Settings.Default.InertialStepSize);
                            });
                            break;
                        case 'R':
                            //motor 1 +
                            //motor 3 +
                            await Task.Run(() =>
                            {
                                tiInterface.MoveMotorInc(1, Properties.Settings.Default.InertialStepSize);
                                tiInterface.MoveMotorInc(3, Properties.Settings.Default.InertialStepSize);
                            });
                            break;
                        case 'L':
                            //motor 1 -
                            //motor 3 -
                            await Task.Run(() =>
                            {
                                tiInterface.MoveMotorInc(1, -Properties.Settings.Default.InertialStepSize);
                                tiInterface.MoveMotorInc(3, -Properties.Settings.Default.InertialStepSize);
                            });
                            break;
                    }
                }
            }
            catch (Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "MirrorButton_Click: unable to move inertial motor: " + x.Message);
            }
            s.IsEnabled = true;
        }
    }
}
