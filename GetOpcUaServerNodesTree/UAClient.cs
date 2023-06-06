using Newtonsoft.Json;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua;

namespace GetOpcUaServerNodesTree;

public class UAClient : IDisposable
{
    #region Cunstructors

    string endpointURL;
    string name;
    string filename;


    public UAClient(string _endpointURL, string _filename = "NodesTree")
    {
        endpointURL = _endpointURL;
        filename = _filename;
        name = "GetMachineClient";
    }
    #endregion


    #region IDisposable
    /// <summary>
    /// Dispose objects.
    /// </summary>
    public void Dispose()
    {
        Disconnect();
    }
    #endregion

    #region StartClient
    public void Stop()
    {
        if (m_session != null)
        {
            m_session.Close();
            m_session.Dispose();
            m_session = null;
        }
    }

    #endregion

    #region Public Properties
    /// <summary>
    /// The reconnect period to be used in ms.
    /// </summary>
    public int ReconnectPeriod { get; set; } = 10000;

    /// <summary>
    /// Auto accept untrusted certificates.
    /// </summary>
    public bool AutoAccept { get; set; } = true;
    #endregion

    #region Public Methods

    /// <summary>
    /// Disconnects the session.
    /// </summary>
    public void Disconnect()
    {
        try
        {
            if (m_session != null)
            {
                Console.WriteLine("Disconnecting...");

                lock (m_lock)
                {
                    m_session.KeepAlive -= Session_KeepAlive;
                    m_reconnectHandler?.Dispose();
                }

                Stop();

                // Log Session Disconnected event
                Console.WriteLine("Session Disconnected.");
            }
            else
            {
                Console.WriteLine("Session not created!");
            }
        }
        catch (Exception ex)
        {
            // Log Error
            Console.WriteLine($"Disconnect Error : {ex.Message}");
        }
    }
    /// <summary>
    /// Handles a keep alive event from a session and triggers a reconnect if necessary.
    /// </summary>
    private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
    {
        try
        {
            // check for events from discarded sessions.
            if (!Object.ReferenceEquals(session, m_session))
            {
                return;
            }

            // start reconnect sequence on communication error.
            if (ServiceResult.IsBad(e.Status))
            {
                if (ReconnectPeriod <= 0)
                {
                    Utils.LogWarning("KeepAlive status {0}, but reconnect is disabled.", e.Status);
                    return;
                }

                lock (m_lock)
                {
                    if (m_reconnectHandler == null)
                    {
                        Utils.LogInfo("KeepAlive status {0}, reconnecting in {1}ms.", e.Status, ReconnectPeriod);
                        Console.WriteLine("--- RECONNECTING {0} ---", e.Status);
                        m_reconnectHandler = new SessionReconnectHandler(true);
                        m_reconnectHandler.BeginReconnect(m_session, ReconnectPeriod, Client_ReconnectComplete);
                    }
                    else
                    {
                        Utils.LogInfo("KeepAlive status {0}, reconnect in progress.", e.Status);
                    }
                }

                return;
            }
        }
        catch (Exception exception)
        {
            Utils.LogError(exception, "Error in OnKeepAlive.");
        }
    }

    /// <summary>
    /// Called when the reconnect attempt was successful.
    /// </summary>
    private void Client_ReconnectComplete(object sender, EventArgs e)
    {
        // ignore callbacks from discarded objects.
        if (!Object.ReferenceEquals(sender, m_reconnectHandler))
        {
            return;
        }

        lock (m_lock)
        {
            // if session recovered, Session property is null
            if (m_reconnectHandler.Session != null)
            {
                m_session = m_reconnectHandler.Session as Session;
            }

            m_reconnectHandler.Dispose();
            m_reconnectHandler = null;
        }

        Console.WriteLine("--- RECONNECTED ---");
    }

    #endregion

    #region Protected Methods
    /// <summary>
    /// Handles the certificate validation event.
    /// This event is triggered every time an untrusted certificate is received from the server.
    /// </summary>
    protected virtual void CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
    {
        bool certificateAccepted = false;

        // ****
        // Implement a custom logic to decide if the certificate should be
        // accepted or not and set certificateAccepted flag accordingly.
        // The certificate can be retrieved from the e.Certificate field
        // ***

        ServiceResult error = e.Error;
        Console.WriteLine(error);
        if (error.StatusCode == StatusCodes.BadCertificateUntrusted && AutoAccept)
        {
            certificateAccepted = true;
        }

        if (certificateAccepted)
        {
            Console.WriteLine("Untrusted Certificate accepted. Subject = {0}", e.Certificate.Subject);
            e.Accept = true;
        }
        else
        {
            Console.WriteLine("Untrusted Certificate rejected. Subject = {0}", e.Certificate.Subject);
        }
    }
    #endregion

    #region Private Fields
    private object m_lock = new();
    private SessionReconnectHandler? m_reconnectHandler;
    private Session? m_session;
    #endregion

    #region GetMachineData

    /// <summary>
    /// <c>GetMachineTreeRecursive</c> implements <c>GetMachineTree</c> in a recursive fashion.
    /// </summary>
    public async Task<bool> GetMachineTreeRecursive()
    {
        #region 1. Clean Initialization of the Client-Server Connection
        // Taken from StartAsync
        ApplicationInstance application = new ApplicationInstance
        {
            ApplicationName = "Get Machine Nodeset Client",
            ApplicationType = ApplicationType.Client,
            ConfigSectionName = "Opc.Ua.Client"
        };

        string configPath = "Opc.Ua.Client.Config.xml";
        // load the application configuration.
        ApplicationConfiguration configuration = await application.LoadApplicationConfiguration(configPath, false);
        Console.WriteLine("1 - Check certificates.");
        // check the application certificate.
        bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(
            false,
            CertificateFactory.DefaultKeySize,
            CertificateFactory.DefaultLifeTime).ConfigureAwait(false);

        if (!haveAppCertificate)
        {
            throw new Exception("Application instance certificate invalid!");
        }

        if (!configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
        {
            configuration.ApplicationUri = X509Utils.GetApplicationUriFromCertificate(configuration.SecurityConfiguration.ApplicationCertificate.Certificate);
            configuration.CertificateValidator.CertificateValidation
            += new CertificateValidationEventHandler(
                CertificateValidation);
        }
        else
        {
            Console.WriteLine("    WARN: missing application certificate, using unsecure connection.");
        }

        Console.WriteLine("2 - Discover endpoints of {0}.", endpointURL);
        var selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointURL, haveAppCertificate, 15000);

        Console.WriteLine("    Selected endpoint uses: {0}",
            selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

        Console.WriteLine("3 - Create a session with OPC UA server.");
        var endpointConfiguration = EndpointConfiguration.Create(configuration);
        var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
        try
        {
            m_session = await Session.Create(configuration, endpoint, false, name, 60000, new UserIdentity(new AnonymousIdentityToken()), null);

            // register keep alive handler
            m_session.KeepAlive += Session_KeepAlive;

            Console.WriteLine("4 - Browse the OPC UA server namespace.");
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;

            references = m_session.FetchReferences(ObjectIds.ObjectsFolder);

            m_session.Browse(
                null,
                null,
                ObjectIds.ObjectsFolder,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                out continuationPoint,
                out references);
            #endregion

            #region 2. Definition of response
            // Attributes that we want to read
            var attributeDict = new Dictionary<uint, DataValue>()
            {
                { Attributes.NodeId, null},
            { Attributes.NodeClass, null},
            { Attributes.BrowseName, null},
            { Attributes.DisplayName, null},
            { Attributes.DataType, null},
        };

            #endregion
            List<OPCData> data = new List<OPCData>();
            #region 3. Assignment of values
            foreach (var refs in references)
            {
                var subTempData = new OPCData();

                subTempData.Child = Traverse(refs, attributeDict, 1, new Dictionary<ReferenceDescription, string>());

                var retValues = NodeRead((NodeId)refs.NodeId, attributeDict);

                // Assign corresponding indices to tempData
                subTempData.NodeType = Enum.Parse<NodeClass>(retValues[1].ToString()).ToString();
                subTempData.BrowseName = retValues[2].ToString();
                subTempData.DisplayName = retValues[3].ToString();
                subTempData.Datatype = retValues[4].ToString();
                subTempData.NodeId = (NodeId)refs.NodeId;

                data.Add(subTempData);
            }

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            System.IO.File.WriteAllText($"{Directory.GetCurrentDirectory()}//{filename}.json", json);
            Console.WriteLine("Done reading.");
            return File.Exists($"{Directory.GetCurrentDirectory()}//{filename}.json");
        }
        catch (Exception e)
        {
            Console.WriteLine("Error in GetMachineTreeRecursive: " + e.Message);
            return false;
        }
        #endregion
    }

    /// <summary>
    /// <c>Traverse</c> is a helper function for <c>GetMachineTreeRecursive</c>
    /// </summary>
    /// <param name="references"></param>
    /// <param name="attributeDict"></param>
    /// <param name="depth"></param>
    /// <param name="visitedReferences"></param>
    /// <returns></returns>
    public List<OPCData> Traverse(ReferenceDescription references, Dictionary<uint, DataValue> attributeDict, int depth, Dictionary<ReferenceDescription, string> visitedReferences)
    {
        // TODO: Decide on a fixed maximum depth
        if (depth > 10 || visitedReferences.ContainsKey(references))
        {
            return new List<OPCData>();
        }
        visitedReferences.Add(references, "visited");

        // Parent holds all child nodes
        var parent = new List<OPCData>();

        // Get all references from the given ReferenceDescription
        ReferenceDescriptionCollection nextReferences = new ReferenceDescriptionCollection();

        byte[] nextCp;
        m_session?.Browse(
            null,
            null,
            ExpandedNodeId.ToNodeId(references.NodeId, m_session.NamespaceUris),
            0u,
            BrowseDirection.Forward,
            ReferenceTypeIds.HierarchicalReferences,
            true,
            (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
        out nextCp,
            out nextReferences);

        foreach (var refs in nextReferences)
        {

            var subTempData = new OPCData();
            subTempData.Child = new List<OPCData>();

            subTempData.Child = Traverse(refs, attributeDict, depth + 1, visitedReferences);

            var retValues = NodeRead((NodeId)refs.NodeId, attributeDict);

            // Assign corresponding indices to tempData
            subTempData.NodeType = Enum.Parse<NodeClass>(retValues[1].ToString()).ToString();
            subTempData.BrowseName = retValues[2].ToString();
            subTempData.DisplayName = retValues[3].ToString();
            subTempData.Datatype = retValues[4].ToString();
            subTempData.NodeId = (NodeId)refs.NodeId;

            parent.Add(subTempData);
        }

        return parent;
    }


    /// <summary>
    /// <c>NodeRead</c> reads the selected attributes from the address space.
    /// </summary>
    /// <param name="node"> The NodeId to read.</param>
    /// <param name="dict"> The dictionary containing Attributes </param>
    public DataValueCollection NodeRead(NodeId node, Dictionary<uint, DataValue> dict)
    {
        // List of attributes
        ReadValueIdCollection itemsToRead = new ReadValueIdCollection();

        foreach (uint attributeId in dict.Keys)
        {
            itemsToRead.Add(new ReadValueId()
            {
                NodeId = node,
                AttributeId = attributeId,
            });
        }

        // Read attributes from the server
        DataValueCollection? values = null;
        DiagnosticInfoCollection? diagnosticInfos = null;

        ResponseHeader responseHeader = m_session.Read(
            null,
            0,
            TimestampsToReturn.Neither,
            itemsToRead,
            out values,
            out diagnosticInfos);

        return values;
    }
    #endregion

}

public class OPCData
{
    #region Public Fields
    public string NodeType { get; set; }
    public NodeId NodeId { get; set; }
    public string BrowseName { get; set; }
    public string DisplayName { get; set; }
    public string Datatype { get; set; }
    public List<OPCData> Child { get; set; }
    #endregion

    #region Constructor
    public OPCData()
    {
        NodeType = NodeClass.Unspecified.ToString();
        NodeId = new NodeId();
        BrowseName = "";
        DisplayName = "";
        Datatype = "";
        Child = new List<OPCData>();
    }
    #endregion
}

