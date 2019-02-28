using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Threading;

namespace HPAFM_Control_1
{
    public class InterfaceDataBox
    {
        SerialPort DBPort = null;
        byte[] serialBuf = new byte[30]; //serial transmit/receive buffer
        const int SerialWait = 20; //wait time in ms to get a response from controller
        const int DBID = 748295; //Unique ID of this controller

        public struct FDPoint
        {
            public float voltage;
            public int pll_ampl;
        }

        public struct PiezoCalPoint
        {
            public float voltage;
            public int ldv_piezo;
            public int ldv_chamber;
        }

        public void InitializeDataBox()
        {
            if (DBPort != null)
            {//already initialized
                throw new ApplicationException("DataBox cannot be initialized as it is already initialized.");
            }

            try
            {
                DBPort = new SerialPort(Properties.Settings.Default.DataBoxPort, 115200, Parity.None, 8, StopBits.One);//high usable baud rate
                DBPort.Open();
            }
            catch (Exception e)
            {//could not open given port
                DBPort = null;
                throw new ApplicationException("DataBox serial port cannot be opened", e);
            }

            sendVarRequest(0); //ask controller ID
            Thread.Sleep(SerialWait); //wait to receive data

            if (DBPort.BytesToRead < 5)
            { //expected response: "[ID][0x12][0x34][0x56][0x78]"
                Exit();
                throw new ApplicationException("DataBox did not return expected number of bytes on ID request.");
            }
            DBPort.Read(serialBuf, 0, 5);
            if (serialBuf[0] != 0)//expected byte indicating a write-out from device
            {
                Exit();
                throw new ApplicationException("DataBox did not return expected ID byte.");
            }

            int id = BitConverter.ToInt32(serialBuf, 1);

            if (id != DBID)
            { //not correct ID
                Exit();
                throw new ApplicationException("DataBox did not return expected ID value.");
            }

            //response OK means we are connected to controller
        }

        private void sendVarRequest(int varID)
        {
            serialBuf[0] = (byte)((byte)varID); //set MSB to zero, indicating read
            DBPort.Write(serialBuf, 0, 1); //send request
        }

        public void Exit()
        {
            if (DBPort == null)
                return;

            //turn off anything that needs to be turned off
            setPiezoStatic(0);
            setkHzwave(false);

            DBPort.Close();
            DBPort = null;
        }

        public void setPiezoStatic(float voltage)
        {
            //Command 0x02
            //set a static voltage 0-10V

            if (DBPort == null)
                throw new ApplicationException("setPiezoStatic: DataBox port is not initialized, cannot continue");

            if (voltage < 0 || voltage > 10)
            {// out of bounds
                throw new ApplicationException("setPiezoStatic: input voltage " + voltage.ToString() + " is out of range, cannot continue");
            }

            serialBuf[0] = (byte)(0x02); //set MSB to one, indicating write
            short vpwm = (short)(voltage * 100);
            byte[] val = BitConverter.GetBytes(vpwm);
            serialBuf[1] = val[0];
            serialBuf[2] = val[1];
            DBPort.Write(serialBuf, 0, 3); //send value
        }

        public void setkHzwave(bool on)
        {
            //Command 0x01
            //turn 1kHz oscillation on/off
            if (DBPort == null)
                throw new ApplicationException("setkHzwave: DataBox port is not initialized, cannot continue");

            serialBuf[0] = (byte)(0x01); //set MSB to one, indicating write
            serialBuf[1] = (byte)(on?1:0);
            DBPort.Write(serialBuf, 0, 2); //send value
        }

        public void doPLLscan(out float phase, out int max_ampl)
        {
            //Command 0x03
            //return [phase, max amplitude, zero amplitude]
            
            phase = -1;
            max_ampl = -1;

            if (DBPort == null)
                throw new ApplicationException("doPLLscan: DataBox port is not initialized, cannot continue");

            sendVarRequest(3); //ask PLL scan

            Thread.Sleep(500); //wait to receive data - a safe 700ms to complete scan

            if (DBPort.BytesToRead < 7)
            { //expected response: "[Cmd][phase x 4][max_ampl x 2]"
                throw new ApplicationException("doPLLscan: not enough bytes in response, BytesToRead = " + DBPort.BytesToRead.ToString());
            }
            DBPort.Read(serialBuf, 0, 7);
            if (serialBuf[0] != (3))//expected byte indicating a write-out from device
            {
                throw new ApplicationException("doPLLscan: incorrect first byte value = " + serialBuf[0].ToString());
            }

            phase = BitConverter.ToSingle(serialBuf, 1);
            max_ampl = BitConverter.ToInt16(serialBuf, 5);
        }

        public int getPLLampl()
        {
            //Command 0x04
            //gets current PLL amplitude, used to test for touching during approach
            if (DBPort == null)
                throw new ApplicationException("getPLLampl: cannot continue as databox port not initialized");

            sendVarRequest(4); //ask PLL amplitude

            Thread.Sleep(10); //wait to receive data

            if (DBPort.BytesToRead < 3)
            { //expected response: "[Cmd][ampl x 2]"
                throw new ApplicationException("getPLLampl: not enough bytes in response, BytesToRead = " + DBPort.BytesToRead.ToString());
            }
            DBPort.Read(serialBuf, 0, 3);
            if (serialBuf[0] != (4))//expected byte indicating a write-out from device
            {
                throw new ApplicationException("getPLLampl: incorrect first byte value = " + serialBuf[0].ToString());
            }

            return BitConverter.ToInt16(serialBuf, 1);
        }

        public List<FDPoint> doFD(float vstart, float vmid)
        {
            //Command 0x05
            //get amplitudes of PLL loop over an FD ramp
            //vtop is starting voltage and should be high=away from sample, vbtm is ending voltage and should be low=close to sample
            if (DBPort == null)
                throw new ApplicationException("doFD: cannot continue as databox port not initialized");

            if (vstart < 0 || vstart > 10 || vmid<0 || vmid>10 || vstart < vmid)
            {// out of bounds
                throw new ApplicationException("doFD: input voltages out of range: " + vstart.ToString() + ", " + vmid.ToString());
            }

            serialBuf[0] = (byte)(0x05); //set MSB to one, indicating write
            short vpwm = (short)(vstart * 100);
            byte[] val = BitConverter.GetBytes(vpwm);
            serialBuf[1] = val[0];
            serialBuf[2] = val[1];
            vpwm = (short)(vmid * 100);
            val = BitConverter.GetBytes(vstart);
            serialBuf[3] = val[0];
            serialBuf[4] = val[1];
            /*val = BitConverter.GetBytes(vmid);
            serialBuf[5] = val[0];
            serialBuf[6] = val[1];
            serialBuf[7] = val[2];
            serialBuf[8] = val[3];*/
            DBPort.Write(serialBuf, 0, 5); //send value

            Thread.Sleep(SerialWait); //wait to receive data

            if (DBPort.BytesToRead < 3)
            { //expected response: "[Cmd][numpts x 2]"
                throw new ApplicationException("doFD: not enough bytes in response, BytesToRead = " + DBPort.BytesToRead.ToString());
            }
            DBPort.Read(serialBuf, 0, 3);
            if (serialBuf[0] != (5))//expected byte indicating a write-out from device
            {
                throw new ApplicationException("doFD: incorrect first byte value = " + serialBuf[0].ToString());
            }

            int numpts = BitConverter.ToInt16(serialBuf, 1);

            //there is too much data to stuff in the buffer so I will receive it point by point
            /*if (DBPort.BytesToRead < 6 * numpts)
            { //all data points must have been received
                return null;
            }*/

            List<FDPoint> fdlist = new List<FDPoint>(numpts);

            for(int i = 0; i < numpts; i++)
            {
                while(DBPort.BytesToRead < 4)
                {
                    Thread.Sleep(1);
                }

                DBPort.Read(serialBuf, 0, 4);
                FDPoint fd;
                fd.voltage = BitConverter.ToInt16(serialBuf, 0) * 0.009775171f; // BitConverter.ToSingle(serialBuf, 0);
                fd.pll_ampl = BitConverter.ToInt16(serialBuf, 2);
                fdlist.Add(fd);
            }

            return fdlist;
        }

        public List<PiezoCalPoint> doPiezoCal(float vstart, float vmid)
        {
            //Command 0x06
            //get differential LF displacement measures
            //vtop is starting voltage and should be high=away from sample, vbtm is ending voltage and should be low=close to sample
            if (DBPort == null)
                throw new ApplicationException("doPiezoCal: cannot continue as databox port not initialized");

            if (vstart < 0 || vstart > 10 || vmid < 0 || vmid > 10)
            {// out of bounds
                throw new ApplicationException("doPiezoCal: input voltages out of range: " + vstart.ToString() + ", " + vmid.ToString());
            }

            serialBuf[0] = (byte)(0x06); //set MSB to one, indicating write
            short vpwm = (short)(vstart * 100);
            byte[] val = BitConverter.GetBytes(vpwm);
            serialBuf[1] = val[0];
            serialBuf[2] = val[1];
            vpwm = (short)(vmid * 100);
            val = BitConverter.GetBytes(vstart);
            serialBuf[3] = val[0];
            serialBuf[4] = val[1];
            /*val = BitConverter.GetBytes(vmid);
            serialBuf[5] = val[0];
            serialBuf[6] = val[1];
            serialBuf[7] = val[2];
            serialBuf[8] = val[3];*/
            DBPort.Write(serialBuf, 0, 5); //send value

            Thread.Sleep(SerialWait); //wait to receive data

            if (DBPort.BytesToRead < 3)
            { //expected response: "[Cmd][numpts x 2]"
                throw new ApplicationException("doPiezoCal: not enough bytes in response, BytesToRead = " + DBPort.BytesToRead.ToString());
            }
            DBPort.Read(serialBuf, 0, 3);
            if (serialBuf[0] != (6))//expected byte indicating a write-out from device
            {
                throw new ApplicationException("doPiezoCal: incorrect first byte value = " + serialBuf[0].ToString());
            }

            int numpts = BitConverter.ToInt16(serialBuf, 1);

            //there is too much data to stuff in the buffer so I will process it point by point
            /*if (DBPort.BytesToRead < 8 * numpts)
            { //all data points must have been received
                return null;
            }*/

            List<PiezoCalPoint> pclist = new List<PiezoCalPoint>(numpts);

            for (int i = 0; i < numpts; i++)
            {
                while(DBPort.BytesToRead < 6)
                {
                    Thread.Sleep(1);
                }

                DBPort.Read(serialBuf, 0, 6);
                PiezoCalPoint pp;
                pp.voltage = BitConverter.ToInt16(serialBuf, 0) * 0.009775171f; // BitConverter.ToSingle(serialBuf, 0);
                pp.ldv_piezo = BitConverter.ToInt16(serialBuf, 2);
                pp.ldv_chamber = BitConverter.ToInt16(serialBuf, 4);
                pclist.Add(pp);
            }

            return pclist;
        }
    }
}
