#region USING

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Open.Nat;

#endregion

namespace PortOpener
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region FIELDS

        private bool _portMapped;
        private NatDevice _natDevice;
        private List<Mapping> _mappings;

        #endregion

        #region CONSTRUCTORS

        public MainWindow()
        {
            InitializeComponent();
        }

        #endregion

        #region METHODS

        private void AddMappings()
        {
            if (_portMapped)
                throw new InvalidOperationException("The port is already mapped. Close it first");

            // Todo : complete port mapping

            string mappingName = "NX_Port_Opener";
            if (!string.IsNullOrWhiteSpace(TxtMappingName.Text))
                mappingName = TxtMappingName.Text.Trim();

            if (string.IsNullOrWhiteSpace(TxtExternalPort.Text))
                throw new ArgumentNullException("External port", "No external port provided");

            if (!int.TryParse(TxtExternalPort.Text.Trim(), out int publicPort))
                throw new InvalidCastException("External port should be an integer");

            if (string.IsNullOrWhiteSpace(TxtInternalPort.Text))
                throw new ArgumentNullException("Internal port", "No internal port provided");

            if (!int.TryParse(TxtInternalPort.Text.Trim(), out int privatePort))
                throw new InvalidCastException("Internal port should be an integer");

            bool openTcp = (bool) ChkOpenTcp.IsChecked;
            bool openUdp = (bool) ChkOpenUdp.IsChecked;

            bool portMapped = false;
            Task t = Task.Run(async () =>
            {
                try
                {
                    await TryPortMappingAsync(publicPort, privatePort, openTcp, openUdp, mappingName);
                    portMapped = true;
                }
                catch (Exception e)
                {
                    MessageBox.Show($"Unable to add mapping : {e.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            t.Wait();

            _portMapped = portMapped;
            TxtExternalPort.IsEnabled = !_portMapped;
            TxtInternalPort.IsEnabled = !_portMapped;
            TxtMappingName.IsEnabled = !_portMapped;
            ChkOpenTcp.IsEnabled = !_portMapped;
            ChkOpenUdp.IsEnabled = !_portMapped;

            if (_portMapped)
                BtnAddRemove.Content = "Remove mapping";
        }

        private void RemoveMappings()
        {
            if (_portMapped)
            {
                Task t = Task.Run(async () => await RemovePortMappingsAsync());
                t.Wait();

                TxtExternalPort.IsEnabled = !_portMapped;
                TxtInternalPort.IsEnabled = !_portMapped;
                TxtMappingName.IsEnabled = !_portMapped;
                ChkOpenTcp.IsEnabled = !_portMapped;
                ChkOpenUdp.IsEnabled = !_portMapped;

                if (!_portMapped)
                    BtnAddRemove.Content = "Add mapping";
            }
            else
            {
                throw new InvalidOperationException("No port mapped");
            }
        }

        private async Task FindDeviceAsync(int millisecondsTimeout = 5000)
        {
            NatDiscoverer discoverer = new NatDiscoverer();
            CancellationTokenSource cts = new CancellationTokenSource(millisecondsTimeout);
            try
            {
                _natDevice = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
            }
            catch (Exception e) when (e is TaskCanceledException || e is NatDeviceNotFoundException)
            {
                if (MessageBox.Show("No UPnP device found. Do you want to look for PMP devices ?", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        _natDevice = await discoverer.DiscoverDeviceAsync(PortMapper.Pmp, cts);
                    }
                    catch (Exception ex) when (ex is TaskCanceledException || ex is NatDeviceNotFoundException)
                    {
                        _natDevice = null;
                        throw new NatDeviceNotFoundException("No PMP device found. No mappable NAT devices available.");
                    }
                }
                else
                    throw new OperationCanceledException("Mappable device search canceled");
            }
        }

        private async Task TryPortMappingAsync(int publicPort, int privatePort, bool openTcp, bool openUdp, string mappingName)
        {
            if (!openTcp && !openUdp)
                throw new Exception("UDP and TCP mapping are both unchecked");

            if (_natDevice == null)
                await FindDeviceAsync();

            List<Mapping> deviceMappings = (await _natDevice.GetAllMappingsAsync()).ToList();

            _mappings ??= new List<Mapping>();
            bool openedTcp = false, openedUdp = false;

            if (openTcp && deviceMappings.Count > 0 && deviceMappings.Any(m => m.Protocol == Protocol.Tcp && publicPort == m.PublicPort))
            {
                MessageBox.Show($"TCP mapping for external port '{publicPort}' already exists", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (openTcp)
            {
                Mapping tcpMapping = new Mapping(Protocol.Tcp, privatePort, publicPort, mappingName + (openUdp ? " - TCP" : ""));
                await _natDevice.CreatePortMapAsync(tcpMapping);
                _mappings.Add(tcpMapping);
                openedTcp = true;
            }

            if (openUdp && deviceMappings.Count > 0 && deviceMappings.Any(m => m.Protocol == Protocol.Udp && publicPort == m.PublicPort))
            {
                MessageBox.Show($"UDP mapping for external port '{publicPort}' already exists", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (openUdp)
            {
                Mapping udpMapping = new Mapping(Protocol.Udp, privatePort, publicPort, mappingName + (openTcp ? " - UDP" : ""));
                await _natDevice.CreatePortMapAsync(udpMapping);
                _mappings.Add(udpMapping);
                openedUdp = true;
            }

            if (!openedTcp && !openedUdp)
                throw new Exception("No ports to open");
        }

        private async Task RemovePortMappingsAsync()
        {
            if (_natDevice != null && _mappings != null && _mappings.Count > 0)
            {
                foreach (Mapping mapping in _mappings)
                    await _natDevice.DeletePortMapAsync(mapping);
                _portMapped = false;
            }
        }

        private void BtnAddRemove_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_portMapped)
                {
                    AddMappings();
                    if (_portMapped)
                        MessageBox.Show($"Mapping '{TxtMappingName.Text.Trim()}' successfully added", "Mapping added", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    RemoveMappings();
                    if (!_portMapped)
                        MessageBox.Show($"Mapping '{TxtMappingName.Text.Trim()}' successfully removed", "Mapping removed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Unable to {(_portMapped ? "remove" : "add")} mapping : {exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_portMapped)
                RemoveMappings();

            base.OnClosing(e);
        }

        #endregion
    }
}