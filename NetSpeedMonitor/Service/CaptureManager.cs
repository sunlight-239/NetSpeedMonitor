﻿using SharpPcap;
using SharpPcap.LibPcap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace USTC.Software.hanyizhao.NetSpeedMonitor
{
    /// <summary>
    /// This class handles the capture threads.
    /// </summary>
    public class CaptureManager
    {
        /// <summary>
        /// Store packets captured into <see cref="UDMap"/>
        /// </summary>
        /// <param name="udMap">Storage</param>
        public CaptureManager(UDMap udMap)
        {
            uploadDownloadMap = udMap;
        }

        /// <summary>
        /// Start the capture threads. This function cost several minutes.
        /// </summary>
        /// <returns>True, success. False, maybe need Winpcap.</returns>
        public bool InitAndStart()
        {
            lock (lockDevices)
            {
                try
                {
                    if(devices == null)
                    {
                        devices = CaptureDeviceList.Instance;
                    }
                    if (devices != null && !started)
                    {
                        NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
                        RestartDevices();
                        started = true;
                    }
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop capture threads. This function costs several minutes.
        /// </summary>
        public void Stop()
        {
            lock(lockDevices)
            {
                if(devices != null && started)
                {
                    foreach (ICaptureDevice i in devices)
                    {
                        StopDevice(i);
                    }
                    delayRunManager.RemoveMission(RefreshDeviceList);
                    started = false;
                }
            }
        }

        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
        {
            Console.WriteLine("NetworkChange Time:" + DateTime.Now.ToLongTimeString());
            delayRunManager.RunAfter(RefreshDeviceList, 5000);
        }

        private void RestartDevices()
        {
            foreach (ICaptureDevice i in devices)
            {
                if (i is LibPcapLiveDevice)
                {
                    var d = i as LibPcapLiveDevice;
                    StartDevice(d);
                }
            }
            RefreshNetworkStructure();
        }

        private void RefreshDeviceList()
        {
            lock (lockDevices)
            {
                DateTime start = DateTime.Now;
                try
                {
                    devices.Refresh();
                }
                catch(Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                DateTime end = DateTime.Now;
                Console.WriteLine("Refresh Time Cost:" + end.Subtract(start).TotalMilliseconds + "ms");
                RestartDevices();
            }
        }

        private void RefreshNetworkStructure()
        {
            List<Network> networks = new List<Network>();
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in networkInterfaces)
            {
                if (adapter.Supports(NetworkInterfaceComponent.IPv4))
                {
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    if (properties.UnicastAddresses.Count > 0)
                    {
                        foreach (UnicastIPAddressInformation oneAddress in properties.UnicastAddresses)
                        {
                            if (oneAddress.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                if (oneAddress.IPv4Mask != null)
                                {
                                    networks.Add(new Network(oneAddress.Address, oneAddress.IPv4Mask));
                                    Console.WriteLine(adapter.Name);
                                    Console.WriteLine(oneAddress.Address);

                                    uint d = BitConverter.ToUInt32(oneAddress.IPv4Mask.GetAddressBytes(), 0);
                                    Console.WriteLine(oneAddress.IPv4Mask + " " + d + " " + BitConverter.ToString(BitConverter.GetBytes(d)) + "\n");
                                }
                            }
                        }
                    }
                }
            }
            networkStructure.RefreshNetworkStructure(networks);
        }

        private void StopDevice(ICaptureDevice device)
        {
            try
            {
                if (device.Started)
                {
                    device.OnPacketArrival -= I_OnPacketArrival;
                    device.StopCapture();
                    device.Close();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

        }

        private void StartDevice(ICaptureDevice device)
        {
            if (!device.Started)
            {
                device.OnPacketArrival += I_OnPacketArrival;
                device.Open(DeviceMode.Normal);
                device.StartCapture();
            }
            else
            {
                StopDevice(device);
                device.OnPacketArrival += I_OnPacketArrival;
                device.Open(DeviceMode.Normal);
                device.StartCapture();
            }
        }
        
        private void I_OnPacketArrival(object sender, CaptureEventArgs e)
        {
            int len = 0;
            TCPUDP protocol = TCPUDP.TCP;
            PacketAddress address = Tool.GetPacketAddressFromRowPacket(e.Packet, ref len, ref protocol);
            if (address != null && len != 0)
            {
                PacketFlow packetFlow = networkStructure.GetPacketFlow(address);
                packetFlow.protocol = protocol;
                uploadDownloadMap.AddPacket(packetFlow, len);
            }
        }

        private CaptureDeviceList devices;
        private object lockDevices = new object();
        private bool started = false;
        
        private NetworkStructure networkStructure = new NetworkStructure(new List<Network>());
        private DelayRunManager delayRunManager = new DelayRunManager();
        private UDMap uploadDownloadMap;

    }
}
