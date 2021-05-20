
using GHLtarUtility.Resources;
using LibUsbDotNet;
using LibUsbDotNet.Main;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;
using System.Linq;
using System.Timers;
using System.Windows.Forms;
using Windows.Security.Authentication.OnlineId;

namespace GHLtarUtility
{
    class PS3Guitar
    {
        public UsbDevice device;
        public IXbox360Controller controller;
        private System.Timers.Timer runTimer;
        System.Threading.Thread t;
        private bool shouldStop;

        public PS3Guitar(UsbDevice dongle, IXbox360Controller newController)
        {
            device = dongle;
            controller = newController;

            // Timer to send control packets
            runTimer = new System.Timers.Timer(10000);
            runTimer.Elapsed += sendControlPacket;
            runTimer.Start();

            // Thread to constantly read inputs
            t = new System.Threading.Thread(new System.Threading.ThreadStart(updateRoutine));
            t.Start();

            controller.Connect();
        }

        public bool isReadable()
        {
            // If device isn't open (closes itself), assume disconnected.
            if (!device.IsOpen) return false;
            if (!device.UsbRegistryInfo.IsAlive) return false;
            return true;
        }

        public void updateRoutine()
        {
            short[] tilt = new short[(10)];
         
            for (int i = 0; i < tilt.Length; i++)
                tilt[i] = 0;
            short sum =0;
            short old_tilt = 0;
            short st_power = 0;
            int pos = 0;
            FixedSizedQueue<int> buffer100 = new FixedSizedQueue<int>(50);
            FixedSizedQueue<int> buffer10 = new FixedSizedQueue<int>(10);

            while (!shouldStop)
            {
                // Read 27 bytes from the guitar
                int bytesRead;
                byte[] readBuffer = new byte[27];
                var reader = device.OpenEndpointReader(ReadEndpointID.Ep01);
                reader.Read(readBuffer, 100, out bytesRead);
                /*foreach(byte i in readBuffer)
                {
                    Console.Write("{0:X2} ", i);
                }
                Console.WriteLine();
                */
                // Prevent default 0x00 when no bytes are read
                if (bytesRead > 0)
                {
                    // Set the fret inputs on the virtual 360 controller
                    byte frets = readBuffer[0];
                    controller.SetButtonState(Xbox360Button.A, (frets & 0x02) != 0x00 || (readBuffer[6] > 0x1F && readBuffer[6]<0x30)); // B1
                    controller.SetButtonState(Xbox360Button.B, (frets & 0x04) != 0x00 || (readBuffer[6] > 0x4F && readBuffer[6] < 0x60)); // B2
                    controller.SetButtonState(Xbox360Button.Y, (frets & 0x08) != 0x00 || (readBuffer[6] > 0xAF && readBuffer[6] <= 0xC0)); // B3
                    controller.SetButtonState(Xbox360Button.X, (frets & 0x01) != 0x00 || (readBuffer[6] > 0x8F && readBuffer[6] < 0xA0)); // W1

                    controller.SetButtonState(Xbox360Button.LeftShoulder, (frets & 0x10) != 0x00 || (readBuffer[6] > 0xEF && readBuffer[6] <= 0xFF)); // W2


                    // Set the strum bar values - can probably be more efficient but eh
                    byte strum = readBuffer[2];
                    if (strum == 0x04)
                    {
                        // Strum Down
                        controller.SetButtonState(Xbox360Button.Down, true);
                        controller.SetAxisValue(Xbox360Axis.LeftThumbY, -32768);
                        controller.SetButtonState(Xbox360Button.Up, false);
                    } else if (strum == 0x00)
                    {
                        // Strum Up
                        controller.SetButtonState(Xbox360Button.Down, false);
                        controller.SetAxisValue(Xbox360Axis.LeftThumbY, 32767);
                        controller.SetButtonState(Xbox360Button.Up, true);
                    } else
                    {
                        // No Strum
                        controller.SetButtonState(Xbox360Button.Down, false);
                        controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
                        controller.SetButtonState(Xbox360Button.Up, false);
                    }



                    // Set the buttons (pause/HP only for now)
                    byte buttons = readBuffer[1];
                    controller.SetButtonState(Xbox360Button.Start, (buttons & 0x02) != 0x00); // Start
                    controller.SetButtonState(Xbox360Button.Back, (buttons & 0x01) != 0x00); // Select Power
                    controller.SetButtonState(Xbox360Button.LeftThumb, (buttons & 0x04) != 0x00); // GHTV Button
                    controller.SetButtonState(Xbox360Button.Guide, (buttons & 0x10) != 0x00); // Sync Button

                    // Set the tilt and whammy

                    //Console.WriteLine((readBuffer[19] << 8));
                    controller.SetAxisValue(Xbox360Axis.RightThumbY, (short)(Int16)((readBuffer[5] << 8) - 32768));
                    //Console.WriteLine(readBuffer[19]);
                    /*controller.SetAxisValue(Xbox360Axis.RightThumbX, (short)((readBuffer[19] << 8)));*/

                    sum -= tilt[pos];
                    tilt[pos] = (short)(readBuffer[19]);
                    sum += tilt[pos];
                    old_tilt = readBuffer[19];
                    pos = (pos + 1) % 10;
                    // pos10 = pos % 10;
                    //Console.WriteLine(sum);


                    buffer100.Enqueue(readBuffer[19]);
                    buffer10.Enqueue(readBuffer[19]);
                    //Console.WriteLine((short)((Int16)((-buffer10.Average() + buffer100.Average())) > 5 ? 32767 : 0));
                    controller.SetAxisValue(Xbox360Axis.RightThumbX, (short)((Int16)(-buffer10.Average() + buffer100.Average())> 5 ? 32767 : 0));
                    /**

                    if (buffer10.Average()<buffer100.Average()-5)
                    {
                        st_power++;
                    }
                    else
                    {
                        st_power = 0;
                        controller.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                    }
                     **/
                    //controller.SetButtonState(Xbox360Button.Back, st_power>0);

                

                    

                    // TODO: Proper D-Pad emulation
                }
            }
        }

        public void sendControlPacket(Object source, ElapsedEventArgs e)
        {
            // Send the control packet (this is what keeps strumming alive)
            byte[] buffer = new byte[9] { 0x02, 0x08, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            int bytesWrote;
            UsbSetupPacket setupPacket = new UsbSetupPacket(0x21, 0x09, 0x0201, 0x0000, 0x0008);
            device.ControlTransfer(ref setupPacket, buffer, 0x0008, out bytesWrote);
        }

        public void destroy()
        {
            // Destroy EVERYTHING.
            shouldStop = true;
            try { controller.Disconnect(); } catch (Exception) { }
            runTimer.Stop();
            runTimer.Dispose();
            t.Abort();
            device.Close();
        }
    }
}
