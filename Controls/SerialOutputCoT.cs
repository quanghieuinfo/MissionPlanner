﻿using Microsoft.Scripting.Utils;
using MissionPlanner.Comms;
using MissionPlanner.Utilities;
using MissionPlanner.Utilities.CoT;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using static IronPython.Modules.MmapModule;

namespace MissionPlanner.Controls
{
    public partial class SerialOutputCoT : Form
    {
        static TcpListener listener;
        static ICommsSerial CoTStream = new SerialPort();
        static double updaterate = 5;
        System.Threading.Thread t12;
        static bool threadrun = false;
        static internal PointLatLngAlt HomeLoc = new PointLatLngAlt(0, 0, 0, "Home");
        static object[,] tabledata;

        public SerialOutputCoT()
        {
            InitializeComponent();

            CMB_serialport.Items.AddRange(SerialPort.GetPortNames());
            CMB_serialport.Items.Add("TCP Host - 14551");
            CMB_serialport.Items.Add("TCP Client");
            CMB_serialport.Items.Add("UDP Host - 14551");
            CMB_serialport.Items.Add("UDP Client");

            CMB_serialport.Items.Add("ATAK MC");

            CMB_updaterate.Text = updaterate + "hz";

            if (threadrun)
            {
                BUT_connect.Text = Strings.Stop;
            }

            MissionPlanner.Utilities.Tracking.AddPage(this.GetType().ToString(), this.Text);
        }

        private void CMB_updaterate_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                updaterate = float.Parse(CMB_updaterate.Text.Replace("hz", ""));
            }
            catch
            {
                CustomMessageBox.Show(Strings.InvalidUpdateRate, Strings.ERROR);
            }
        }

        private void BUT_connect_Click(object sender, EventArgs e)
        {

            if (listener != null)
            {
                listener.Stop();
                listener = null;
            }

            if (threadrun)
            {
                threadrun = false;
                if (CoTStream != null && CoTStream.IsOpen)
                {
                    CoTStream.Close();
                }
                BUT_connect.Text = Strings.Connect;
                return;
            }

            try
            {
                switch (CMB_serialport.Text)
                {
                    case "TCP Host - 14551":
                    case "TCP Host":
                        CoTStream = new TcpSerial();
                        CMB_baudrate.SelectedIndex = 0;
                        listener = new TcpListener(System.Net.IPAddress.Any, 14551);
                        listener.Start(0);
                        listener.BeginAcceptTcpClient(new AsyncCallback(DoAcceptTcpClientCallback), listener);
                        BUT_connect.Text = Strings.Stop;
                        break;
                    case "TCP Client":
                        CoTStream = new TcpSerial() { retrys = 999999, autoReconnect = true };
                        CMB_baudrate.SelectedIndex = 0;
                        break;
                    case "UDP Host - 14551":
                        CoTStream = new UdpSerial();
                        CMB_baudrate.SelectedIndex = 0;
                        break;
                    case "UDP Client":
                        CoTStream = new UdpSerialConnect();
                        CMB_baudrate.SelectedIndex = 0;
                        break;
                    case "ATAK MC":
                        CoTStream = new UdpSerialConnect() { ConfigRef = "ATAK" };
                        ((UdpSerialConnect)CoTStream).Open("239.2.3.1", "6969");
                        CMB_baudrate.SelectedIndex = 0;
                        break;
                    default:
                        CoTStream = new SerialPort();
                        CoTStream.PortName = CMB_serialport.Text;
                        break;
                }
            }
            catch
            {
                CustomMessageBox.Show(Strings.InvalidPortName);
                return;
            }
            try
            {
                CoTStream.BaudRate = int.Parse(CMB_baudrate.Text);
            }
            catch
            {
                CustomMessageBox.Show(Strings.InvalidBaudRate);
                return;
            }
            try
            {
                if (listener == null)
                    System.Threading.ThreadPool.QueueUserWorkItem(background_DoOpen);
            }
            catch
            {
                CustomMessageBox.Show("Error Connecting\nif using com0com please rename the ports to COM??");
                return;
            }

            t12 = new System.Threading.Thread(new System.Threading.ThreadStart(mainloop))
            {
                IsBackground = true,
                Name = "CoT output"
            };
            t12.Start();

            BUT_connect.Text = Strings.Stop;
        }

        void background_DoOpen(object state)
        {
            if (CoTStream == null)
            {
                return;
            }

            try
            {
                CoTStream.Open();
            }
            catch { CoTStream = null; } // don't care if we crash
        }

        void mainloop()
        {
            threadrun = true;

            int counter = 0;
            while (threadrun)
            {
                try
                {
                    string view = "";
                    MainV2.Comports.ForEach(port => {
                        port.MAVlist.ForEach(mav =>
                        {
                            String xmlStr = getXmlString(mav.sysid, mav.compid);
                            view += xmlStr;

                            if (CoTStream != null && CoTStream.IsOpen)
                            {
                                CoTStream.WriteLine(xmlStr);
                            }
                        });
                    });

                    if (TB_output.IsHandleCreated)
                        TB_output.Invoke((Action)delegate
                        {
                            TB_output.Text = view;
                        }); 

                    var nextsend = DateTime.Now.AddMilliseconds(1000 / updaterate);
                    var sleepfor = Math.Min((int)Math.Abs((nextsend - DateTime.Now).TotalMilliseconds), 4000);
                    Thread.Sleep(sleepfor);
                    counter++;

                        
                }
                catch
                {
                    Thread.Sleep(10);
                }
            }
        }

        void DoAcceptTcpClientCallback(IAsyncResult ar)
        {
            // Get the listener that handles the client request.
            TcpListener listener = (TcpListener)ar.AsyncState;

            try
            {
                // End the operation and display the received data on  
                // the console.
                TcpClient client = listener.EndAcceptTcpClient(ar);

                ((TcpSerial)CoTStream).client = client;

                listener.BeginAcceptTcpClient(new AsyncCallback(DoAcceptTcpClientCallback), listener);
            }
            catch { }
        }

        private String getXmlString(byte sysid, byte compid)
        {
            double lat = MainV2.comPort.MAVlist[sysid,compid].cs.lat;
            double lng = MainV2.comPort.MAVlist[sysid, compid].cs.lng;
            double altitude = MainV2.comPort.MAVlist[sysid, compid].cs.altasl;
            double groundSpeed = MainV2.comPort.MAVlist[sysid, compid].cs.groundspeed;
            double groundcourse = MainV2.comPort.MAVlist[sysid, compid].cs.groundcourse;
            String how = "m-g";

            String xmlStr = getXmlString(FindUIDviaSysid(sysid), TB_xml_type.Text, how, lat, lng, altitude, groundcourse, groundSpeed);

            return xmlStr;
        }

        string FindUIDviaSysid(byte sysid) 
        {
            var rcnt = myDataGridView1.Rows.Count;
            for (int x = 0; x < rcnt - 1; x++)
                if (myDataGridView1[this.sysid.Index, x].Value?.ToString() == sysid.ToString())
                    return myDataGridView1[this.UID.Index, x].Value?.ToString();

            return "NOsysid" + sysid;
        }

        String getXmlString(String uid, String type, String how, double lat, double lng, double alt, double course = -1, double speed = -1)
        {
            // Cursor-on-Target spec
            // https://www.mitre.org/sites/default/files/pdf/09_4937.pdf

            // MIL-STD-2525, needed for event->type
            // https://www.jcs.mil/Portals/36/Documents/Doctrine/Other_Pubs/ms_2525d.pdf

            if (uid == null || uid.Length <= 0) {
                uid = "";
            }
            if (type == null || type.Length <= 0) {
                type = "";
            }

            CultureInfo culture = new CultureInfo("en-US");
            culture.NumberFormat.NumberGroupSeparator = "";

            DateTime time = DateTime.UtcNow;

            var cotevent = new @event()
            {
                uid = uid, type = type, time = time.ToString("o"), start = time.AddSeconds(-5).ToString("o"),
                stale = time.AddSeconds(5).ToString("o"), how = how,
                detail = new detail()
                {
                    track = new track()
                    {
                        course = course.ToString("N2", culture), speed = speed.ToString("N2", culture)
                    }

                },
                point = new point()
                {
                    lat = lat.ToString("N7", culture), lon = lng.ToString("N7", culture), hae = alt.ToString("N2", culture).PadLeft(5, ' ')
                }
            };

            using(StringWriter textWriter = new Utf8StringWriter())
            {
                XmlWriterSettings xws = new XmlWriterSettings();
                xws.OmitXmlDeclaration = false;
                xws.Indent = true;
                xws.Encoding = Encoding.UTF8;
                xws.NewLineOnAttributes = true;

                //Create our own namespaces for the output
                XmlSerializerNamespaces ns = new XmlSerializerNamespaces();

                //Add an empty namespace and empty value
                ns.Add("", "");

                var xtw = XmlTextWriter.Create(textWriter, xws);
                // Then we can set our indenting options (this is, of course, optional).
                XmlSerializer serializer =
                    new XmlSerializer(typeof(@event));

                xtw.WriteStartDocument(true);          
       
                serializer.Serialize(xtw, cotevent, ns);      

                var ans = textWriter.ToString();

                return ans;
            }
            
            StringBuilder sb = new StringBuilder();

            sb.AppendLine  ("<?xml version='1.0' encoding='UTF-8' standalone='yes'?>");
            sb.AppendLine  ("<event version=\"2.0\"");

            sb.AppendFormat("    uid=\"{0}\"", uid); sb.AppendLine();
            sb.AppendFormat("    type=\"{0}\"", type); sb.AppendLine();  // CoT spec section 2.3, additional values by MIL-STD-2525

            sb.AppendFormat("    time=\"{0}\"", time.ToString("o")); sb.AppendLine(); // time stamp: when the event was generated
            sb.AppendFormat("    start=\"{0}\"", time.AddSeconds(-5).ToString("o")); sb.AppendLine(); // starting time when an event should be considered valid
            sb.AppendFormat("    stale=\"{0}\"", time.AddSeconds(5).ToString("o")); sb.AppendLine(); // ending time when an event should no longer be considered valid

            // See Appendix A
            // where h- means human and m- means machine
            // m-g      == h.gps
            // h-p      == h.pasted
            // m-f      == h.fused
            // m-n      == h.ins
            // m-g-n    == h.ins-gps
            // m-g-d    == h.dgps
            sb.AppendFormat("    how=\"{0}\">", how); sb.AppendLine(); // Gives a hint about how the coordinates were generated


            sb.AppendLine  ("  <detail>");
            if (course >= 0 && course <= 360 && speed >= 0) {
                sb.AppendFormat(culture, "    <track course=\"{0:N2}\" speed=\"{1:N2}\" />", course, speed); sb.AppendLine();
            }
            sb.AppendLine  ("  </detail>");

            // hae = Height above the WGS ellipsoid in meters
            // ce = Circular 1-sigma or decimal a circular area about the point in meters
            // le = Linear 1-sigma error or decimal an attitude range about the point in meters
            sb.AppendFormat(culture, "  <point lat=\"{0:N7}\" lon=\"{1:N7}\" hae=\"{2,5:N2}\" ce=\"1.0\" le=\"1.0\"/>", lat, lng, alt); sb.AppendLine();
            sb.AppendLine  ("</event>");

            return sb.ToString();
            
        }

        private void BTN_clear_TB_Click(object sender, EventArgs e)
        {
            TB_output.Text = "";
        }

        private void SerialOutputCoT_Load(object sender, EventArgs e)
        {
            myDataGridView1.Deserialize(Settings.Instance["CoTUID"]);
        }

        private void SerialOutputCoT_FormClosing(object sender, FormClosingEventArgs e)
        {
            
        }

        private void myDataGridView1_RowValidated(object sender, DataGridViewCellEventArgs e)
        {
            Settings.Instance["CoTUID"] = myDataGridView1.Serialize();
        }
    }

    public sealed class Utf8StringWriter : StringWriter
    { public override Encoding Encoding => Encoding.UTF8;
    }
}