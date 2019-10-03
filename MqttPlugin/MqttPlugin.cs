﻿/*
  Copyright (C) 2014 Birunthan Mohanathas

  This program is free software; you can redistribute it and/or
  modify it under the terms of the GNU General Public License
  as published by the Free Software Foundation; either version 2
  of the License, or (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
*/

using System;
using System.Collections.Generic;
using System.Collections;
using System.Runtime.InteropServices;
using Rainmeter;

using System.Text;
using System.Net;
#if !(WINDOWS_APP || WINDOWS_PHONE_APP)
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
#endif
using System.Threading;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Exceptions;
using uPLibrary.Networking.M2Mqtt.Messages;
using uPLibrary.Networking.M2Mqtt.Session;
using uPLibrary.Networking.M2Mqtt.Utility;
using uPLibrary.Networking.M2Mqtt.Internal;


// Overview: This example demonstrates a basic implementation of a parent/child
// measure structure. In this particular example, we have a "parent" measure
// which contains the values for the options "ValueA", "ValueB", and "ValueC".
// The child measures are used to return a specific value from the parent.

// Use case: You could, for example, have a "main" parent measure that queries
// information some data set. The child measures can then be used to return
// specific information from the data queried by the parent measure.

// Sample skin:
/*
    [Rainmeter]
    Update=1000
    BackgroundMode=2
    SolidColor=000000

    [mParent]
    Measure=Plugin
    Plugin=ParentChild.dll
    ValueA=111
    ValueB=222
    ValueC=333
    Type=A

    [mChild1]
    Measure=Plugin
    Plugin=ParentChild.dll
    ParentName=mParent
    Type=B

    [mChild2]
    Measure=Plugin
    Plugin=ParentChild.dll
    ParentName=mParent
    Type=C

    [Text]
    Meter=STRING
    MeasureName=mParent
    MeasureName2=mChild1
    MeasureName3=mChild2
    X=5
    Y=5
    W=200
    H=55
    FontColor=FFFFFF
    Text="mParent: %1#CRLF#mChild1: %2#CRLF#mChild2: %3"
*/

namespace MqttPlugin
{
    internal class Measure
    {
        public string inputStr; //The string returned in GetString is stored here
        public IntPtr buffer; //Prevent marshalAs from causing memory leaks by clearing this before assigning
        public bool IsConnected;

        internal virtual void Dispose()
        {
        }

        internal virtual void Reload(Rainmeter.API api, ref double maxValue)
        {
        }

        internal virtual double Update()
        {
            return 0.0;
        }

        internal virtual String GetString(String topic) {
            return "";
        }
        internal virtual String GetString() {
            return "";
        }
        internal virtual void ExecuteBang(String args) {
        }
        internal virtual void Publish(String topic, String value, byte qos = 0, bool retain = false) {
        }
    }

    internal class ParentMeasure : Measure {
        // This list of all parent measures is used by the child measures to find their parent.
        internal static List<ParentMeasure> ParentMeasures = new List<ParentMeasure>();

        internal string Name;
        internal IntPtr Skin;

        internal String Server;
        internal ushort Port;
        internal String Username;
        internal String Password;
        internal String ClientId;

        internal MqttClient Client { get; }
        public bool IsConnected => Client.IsConnected;
        Rainmeter.API Rainmeter { get; set;  }

        // All Topics of the Parent and Child Measures
        Hashtable Topics = new Hashtable();
        Hashtable Qos = new Hashtable();

        // The Topic of the Parent Measure
        String Topic;

        internal ParentMeasure(Rainmeter.API api) {
            if (Client?.IsConnected == true) {
                Client.Disconnect();
                api.Log(API.LogType.Notice, "Removing existing connection to " + Server + ":" + Port);
                Client = null;
                Hashtable Topics = new Hashtable();
                Hashtable Qos = new Hashtable();
            }

            ParentMeasures.Add(this);
            Rainmeter = api;
            Topic = api.ReadString("Topic", "defaulttopic");
            Name = api.GetMeasureName();
            Skin = api.GetSkin();
            Server = api.ReadString("Server", "");
            int port = api.ReadInt("Port", 1883);
            ClientId = api.ReadString("ClientId", Guid.NewGuid().ToString());
            Username = api.ReadString("Username", "");
            Password = api.ReadString("Password", "");

            Port = 1883;
            if (port <= 0 || port > 65535) {
                api.Log(API.LogType.Warning, "Illegal MQTT port " + port.ToString() + ". Using default port 1883.");
            } else {
                Port = (ushort)port;
            }
            
            try {
                Client = new MqttClient(Server, Port, false, null, null, MqttSslProtocols.None);
                // api.Log(API.LogType.Debug, "Created new MQTT client instance"); // OK
            }
            catch {
                api.Log(API.LogType.Error, "Failed to instantiate MQTT client");
            }

            if (Client == null) {
                api.Log(API.LogType.Error, "MQTT Client is null");
            }

            // Setup callback routines
            Client.MqttMsgPublishReceived += client_MqttMsgPublishReceived;
            Client.MqttMsgSubscribed += client_MqttMsgSubscribed;
            Client.MqttMsgUnsubscribed += client_MqttMsgUnsubscribed;
            Client.MqttMsgPublished += client_MqttMsgPublished;
            Client.ConnectionClosed += (sender, args) => {
                Rainmeter.Log(API.LogType.Warning, "Connection was Closed."); // OK

                //Rainmeter.Log(API.LogType.Warning, "Connection was Closed. Reconnecting..."); // OK
                // Reconnect();
            };

            try {
                Client.Connect(ClientId, Username, Password, false, 60);
            }
            catch {
                api.Log(API.LogType.Error, "Exception while connecting to " + Server + ":" + Port);
            }

            if (Client.IsConnected == false) {
                api.Log(API.LogType.Warning, "Client failed to connect to " + Server + ":" + Port);     // OK
            } else {
                api.Log(API.LogType.Notice, "MQTT Client is connected to " + Server + ":" + Port);      // OK
            }

            // string[] topic = { "sensor/temp", "sensor/humidity" };
            // byte[] qosLevels = { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE };
            // Client.Subscribe(topic, qosLevels);

            // Client.Publish("sensor/temp", Encoding.UTF8.GetBytes("6"));

            void client_MqttMsgPublished(object sender, MqttMsgPublishedEventArgs e) {
                if (e.IsPublished) {
                    Rainmeter.Log(API.LogType.Notice, "Published message ID " + e.MessageId);
                } else {
                    Rainmeter.Log(API.LogType.Warning, "Failed to Publish message ID " + e.MessageId);
                }
            }

            void client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e) {
                String msg = System.Text.Encoding.Default.GetString(e.Message);

                Rainmeter.Log(API.LogType.Debug, "Received topic " + e.Topic + " => " + msg); // OK
                //this.topic = topic;
                //this.message = message;
                //this.dupFlag = dupFlag;
                //this.qosLevel = qosLevel;
                //this.retain = retain;

                if (Topics.Contains(e.Topic)) {
                    Topics[e.Topic] = msg;
                } else {
                    Topics.Add(e.Topic, msg);
                }

                //Rainmeter.Execute("[!UpdateMeasure mqttServer][!UpdateMeter Text][!Redraw]");
            }

            void client_MqttMsgUnsubscribed(object sender, MqttMsgUnsubscribedEventArgs e) {
                Rainmeter.Log(API.LogType.Debug, "Unsubscribed to message ID " + e.MessageId);
            }

            void client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e) {
                Rainmeter.Log(API.LogType.Debug, "Subscribed to message ID " + e.MessageId);
            }
        }

        internal void Reconnect() {
            try {
                Client.Connect(ClientId, Username, Password, false, 60);
            }
            catch {
                Rainmeter.Log(API.LogType.Error, "Exception while reconnecting to " + Server + ":" + Port);
            }

            if (Client.IsConnected == false) {
                Rainmeter.Log(API.LogType.Warning, "Client failed to reconnect to " + Server + ":" + Port);     // OK
            } else {
                Rainmeter.Log(API.LogType.Notice, "MQTT Client is reconnected to " + Server + ":" + Port);      // OK
            }
        }

        internal override void Dispose() {
            if (Client != null && Client.IsConnected == true) {
                try {
                    Client.Disconnect();
                }
                catch {
                    Rainmeter.Log(API.LogType.Error, "Exception while disposing of " + Server + ":" + Port);
                }
            }
            ParentMeasures.Remove(this);
        }

        internal override void Reload(Rainmeter.API api, ref double maxValue) {
            Rainmeter = api;

            // api.Log(API.LogType.Debug, "Reloading");    // OK
            base.Reload(api, ref maxValue);
            api.Log(API.LogType.Debug, "Reloaded");     // OK
        }

        internal void Subscribe(String topic, byte qos) {
            if (!Qos.Contains(topic)) {
                Qos.Add(topic, qos);
            }

            String[] topics = new String[Qos.Count];
            byte[] qosLevels = new Byte[Qos.Count];
            Qos.Keys.CopyTo(topics, 0);
            Qos.Values.CopyTo(qosLevels, 0);

            if (Client.IsConnected) {
                Rainmeter.Log(API.LogType.Debug, "Subscibe to topic " + topic);
                Client.Subscribe(topics, qosLevels);
            } else {
                Rainmeter.Log(API.LogType.Debug, "Client is disconnected, can't subscibe to topic " + topic);
            }
        }

        internal override void Publish(String topic, String value, byte qos=0, bool retain=false) {
            if (Client != null) {
                if (Client.IsConnected) {
                    Rainmeter.Log(API.LogType.Debug, "Publish " + topic + " = " + value);
                    Client.Publish(topic, Encoding.UTF8.GetBytes(value), qos, retain);
                } else {
                    Rainmeter.Log(API.LogType.Debug, "Client is disconnected, can't publish to topic " + topic);
                }
            }
        }

        internal override void ExecuteBang(String args) {
            Publish(Topic, args, 0, false);
        }

        internal override double Update()
        {
            if (Client != null) {
                if (Client.IsConnected == false) {
                    Rainmeter.Log(API.LogType.Warning, "Client connection to " + Server + ":" + Port + " lost. Reconnecting...");     // OK
                    Reconnect();
                }

                Rainmeter.Log(API.LogType.Debug, "Update " + Topic);
                return GetValue();

            } else {
                return 0.0;
            }
        }

        internal override String GetString(String topic) {
            if (Client != null && Client.IsConnected) {
                if (Topics.ContainsKey(topic)) {
                    Rainmeter.Log(API.LogType.Debug, "GetString " + topic);
                    String value = Topics[topic].ToString();

                    return Topics[topic].ToString();
                } else {
                    Rainmeter.Log(API.LogType.Debug, "GetString " + topic + " not found");
                }
            }

            // MeasureType.Major, MeasureType.Minor, and MeasureType.Number are
            // numbers. Therefore, null is returned here for them. This is to
            // inform Rainmeter that it can treat those types as numbers.

            return null;
        }

        internal double GetValue()
        {
            //Rainmeter.Log(API.LogType.Debug, "GetValue");
            return 0.0;
        }
    }

    internal class ChildMeasure : Measure
    {
        private ParentMeasure ParentMeasure = null;
        // The Topic of the Child Measure
        String Topic;
        Rainmeter.API Rainmeter;

        internal ChildMeasure(Rainmeter.API api) {
            Rainmeter = api;
        }

        internal override void Reload(Rainmeter.API api, ref double maxValue)
        {
            Topic = api.ReadString("Topic", "defaulttopic");
            var qos = api.ReadInt("Qos", 0);
            base.Reload(api, ref maxValue);

            string parentName = api.ReadString("ParentName", "");
            IntPtr skin = api.GetSkin();

            // Find parent using name AND the skin handle to be sure that it's the right one.
            ParentMeasure = null;
            foreach (ParentMeasure parentMeasure in ParentMeasure.ParentMeasures)
            {
                if (parentMeasure.Skin.Equals(skin) && parentMeasure.Name.Equals(parentName))
                {
                    ParentMeasure = parentMeasure;
                    ParentMeasure.Subscribe(Topic, (byte)qos);
                }
            }

            if (ParentMeasure == null)
            {
                api.Log(API.LogType.Error, "ParentChild.dll: ParentName=" + parentName + " not valid");
            }
        }

        internal override void ExecuteBang(String args) {
            if (ParentMeasure != null) {
                ParentMeasure.Publish(Topic, args);
            }
        }

        internal override double Update()
        {
            if (ParentMeasure != null)
            {
                return ParentMeasure.GetValue();
            }

            return 0.0;
        }
        internal override String GetString() {
            if (ParentMeasure != null) {
                return ParentMeasure.GetString(Topic);
            }

            return "";
        }
        internal override void Publish(String topic, String value, byte qos = 0, bool retain = false) {
            if (ParentMeasure != null) {
                ParentMeasure.Publish(topic, value, qos, retain);
            }
        }

    }

    public static class Plugin
    {
        static IntPtr StringBuffer = IntPtr.Zero;
        static Rainmeter.API Rainmeter;

        [DllExport]
        public static void Initialize(ref IntPtr data, IntPtr rm)
        {
            Rainmeter.API api = new Rainmeter.API(rm);
            Rainmeter = api;
            string parent = api.ReadString("ParentName", "");

            Measure measure;
            if (String.IsNullOrEmpty(parent))
            {
                measure = new ParentMeasure(api);
            } else
            {
                measure = new ChildMeasure(api);
            }

            data = GCHandle.ToIntPtr(GCHandle.Alloc(measure));
            // Rainmeter.Log(API.LogType.Debug, "Plugin Initialize"); // OK
        }

        [DllExport]
        public static void Finalize(IntPtr data)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            measure.Dispose();
            GCHandle.FromIntPtr(data).Free();
            if (StringBuffer != IntPtr.Zero) {
                Marshal.FreeHGlobal(StringBuffer);
                StringBuffer = IntPtr.Zero;
            }
            Rainmeter = null;
        }

        [DllExport]
        public static void Reload(IntPtr data, IntPtr rm, ref double maxValue)
        {
            // Rainmeter.Log(API.LogType.Debug, "Plugin Reload 1"); // OK

            Rainmeter.API api = new Rainmeter.API(rm);
            // api.Log(API.LogType.Debug, "Plugin Reload 2"); // OK

            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            measure.Reload(api, ref maxValue);
        }

        [DllExport]
        public static double Update(IntPtr data)
        {
            // Rainmeter.Log(API.LogType.Debug, "Plugin Update"); // OK

            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            return measure.Update();
        }

        [DllExport]
        public static IntPtr GetString(IntPtr data) {
            // Rainmeter.Log(API.LogType.Debug, "Plugin GetString"); // OK

            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            if (StringBuffer != IntPtr.Zero) {
                Marshal.FreeHGlobal(StringBuffer);
                StringBuffer = IntPtr.Zero;
            }

            string stringValue = measure.GetString();
            if (stringValue != null) {
                StringBuffer = Marshal.StringToHGlobalUni(stringValue);
            }

            return StringBuffer;
        }

        [DllExport]
        public static void ExecuteBang(IntPtr data, [MarshalAs(UnmanagedType.LPWStr)]String args)
        {
            Rainmeter.Log(API.LogType.Debug, "Plugin Execute Bang");

            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            measure.ExecuteBang(args);
        }


        [DllExport]
        public static IntPtr Publish(IntPtr data, int argc,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 1)] string[] argv) {
            // Rainmeter.Log(API.LogType.Debug, "Plugin Publish"); // OK

            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            if (measure.buffer != IntPtr.Zero) {
                Marshal.FreeHGlobal(measure.buffer);
                measure.buffer = IntPtr.Zero;
            }

            //If we are given one or more arguments convert to uppercase the first one
            if (argc == 2) {
                measure.Publish(argv[0], argv[1]);
                measure.buffer = Marshal.StringToHGlobalUni("Pub");
            }
            //If we are given no arguments  convert to uppercase the string we recived with the input option
            else {
                measure.Publish("atopic", "avalue");
                measure.buffer = Marshal.StringToHGlobalUni("Arg count must be 2");
            }

            return Marshal.StringToHGlobalUni("");
        }

    }
}
