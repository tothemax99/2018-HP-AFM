using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO.Ports;

namespace HPAFM_Control_1
{
    public class InterfacePressureController
    {
        //com port of pressure controller defined in project settings
        const int MaxPressure = 3000; //maximum pressure setpoint in PSI
        const int SerialWait = 50; //wait time in ms to get a response from controller (40 ms is tested minimal)
        SerialPort PCPort = null;

        public void InitializePressureController()
        {
            if (PCPort != null)
                throw new ApplicationException("Pressure Controller cannot be initialized as it is already initialized.");

            try
            {
                PCPort = new SerialPort(Properties.Settings.Default.PressureControllerPort, 19200, Parity.None, 8, StopBits.One);
                PCPort.Open();
            }
            catch (Exception e)
            {
                PCPort = null;
                throw new ApplicationException("Pressure Controller port could not be opened.", e);
            }

            PCPort.NewLine = "\r"; //the controller expects and outputs a [CR] only

            PCPort.WriteLine("AR91"); //ask to read parameter 91 (update rate) should be 50 nominally
            Thread.Sleep(SerialWait); //wait to receive data

            if (PCPort.BytesToRead != 13)
            { //expected response: "A   091 = 50[CR]"
                Exit();
                throw new ApplicationException("PCPort too few bytes responding to ID request.");
            }

            string rsp = PCPort.ReadLine();
            if (!rsp.Equals("A   091 = 50")) //this is the expected (default) value and format of parameter 91, if response OK means we are connected to controller
            {
                Exit();
                throw new ApplicationException("PCPort incorrect value responding to ID request.");
            }
        }

        public void SetPressure(int psi)
        {
            if (PCPort == null)
                throw new ApplicationException("SetPressure: PCPort is not initialized, cannot continue");

            if (psi < 0 || psi > MaxPressure)
                throw new ArgumentOutOfRangeException("SetPressure: pressure input is outside valid range, setpoint=" + psi.ToString());

            StringBuilder msg = new StringBuilder("AS");
            msg.Append(psi);

            PCPort.WriteLine(msg.ToString()); //set new pressure setpoint, will get response automatically
            Thread.Sleep(SerialWait); //wait to receive data
            if (PCPort.BytesToRead != 17)
            { //expected response: "A +000000 000000[CR]"
                throw new ApplicationException("SetPressure: not enough bytes in response, BytesToRead = " + PCPort.BytesToRead.ToString());
            }
            string rsp = PCPort.ReadLine();
            //response is formatted as "A +000000 000000" first number is current, second is setpoint pressure
            if(int.Parse(rsp.Substring(10)) != psi) //see if device's setpt equals ours
                throw new ApplicationException("SetPressure: actual setpoint " + int.Parse(rsp.Substring(10)).ToString() + " does not equal desired " + psi.ToString());
        }

        public int GetSetpt()
        {//returns actual current set point in PSI
            if (PCPort == null)
                throw new ApplicationException("GetSetpt: PCPort is not initialized, cannot continue");

            PCPort.WriteLine("A"); //ask status
            Thread.Sleep(SerialWait); //wait to receive data
            if (PCPort.BytesToRead != 17)
            { //expected response: "A +000000 000000[CR]"
                throw new ApplicationException("GetSetpt: not enough bytes in response, BytesToRead = " + PCPort.BytesToRead.ToString());
            }
            string rsp = PCPort.ReadLine();
            //response is formatted as "A +000000 000000" first number is current, second is setpoint pressure
            return int.Parse(rsp.Substring(10));
        }

        public int GetCurrentPressure()
        {//returns measured pressure at controller outlet in PSI
            if (PCPort == null)
                throw new ApplicationException("GetCurrentPressure: PCPort is not initialized, cannot continue");

            PCPort.WriteLine("A"); //ask status
            Thread.Sleep(SerialWait); //wait to receive data
            if (PCPort.BytesToRead != 17)
            { //expected response: "A +000000 000000[CR]"
                throw new ApplicationException("GetCurrentPressure: not enough bytes in response, BytesToRead = " + PCPort.BytesToRead.ToString());
            }
            string rsp = PCPort.ReadLine();
            //response is formatted as "A +000000 000000" first number is current, second is setpoint pressure
            return int.Parse(rsp.Substring(2,7));
        }

        public void Exit()
        {
            if (PCPort == null)
                return;

            //not desirable to release pressure with a low setpoint, so just exit as-is

            PCPort.Close();
            PCPort = null;
        }
    }
}
