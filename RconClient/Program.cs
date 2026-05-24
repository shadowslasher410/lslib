using LSLib.Rcon;
using System.Net;

if (args.Length < 2)
{
    var originalColor = Console.ForegroundColor;
    try
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("Usage: Rcon <ip:port> <command> [<args> ...]");
    }
    finally
    {
        Console.ForegroundColor = originalColor;
    }
    Environment.Exit(1);
}

ReadOnlySpan<string> argumentSpan = args;
var ipPortSegments = argumentSpan[0].Split(':');

if (ipPortSegments.Length < 2 || !int.TryParse(ipPortSegments[1], out int targetPort))
{
    throw new ArgumentException("Invalid connection format configuration parameter. Expected format: 'IP:Port'.");
}

string targetCommand = argumentSpan[1];
string[] commandArguments = new string[argumentSpan.Length - 2];
Array.Copy(args, 2, commandArguments, 0, argumentSpan.Length - 2);

bool isCommandExecuted = false;
int activityHeartbeatCounter = 0;
RakNetSession? operationalSession = null;
using var applicationShutdownCts = new CancellationTokenSource();

var socket = new RakNetSocket();
socket.SessionEstablished += OnSessionEstablished;

_ = Task.Run(async () =>
{
    using var periodicHeartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(3));

    while (await periodicHeartbeatTimer.WaitForNextTickAsync(applicationShutdownCts.Token))
    {
        if (Interlocked.Exchange(ref activityHeartbeatCounter, 0) == 0)
        {
            if (operationalSession is not null)
            {
                Console.WriteLine("Inactivity threshold reached. Dispatching disconnection console request command...");
                var disconnectCmd = new DosDisconnectConsole();
                operationalSession.SendEncapsulated(disconnectCmd, EncapsulatedReliability.ReliableOrdered);
            }
            else
            {
                var errorColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("Timeout restriction reached waiting for initial RakNet connection handshake session establishment.");
                }
                finally
                {
                    Console.ForegroundColor = errorColor;
                }
                Environment.Exit(1);
            }
            break;
        }
    }
}, applicationShutdownCts.Token);

IPEndPoint targetServerEndpoint = new(IPAddress.Parse(ipPortSegments[0]), targetPort);
await socket.BeginConnectionAsync(targetServerEndpoint, applicationShutdownCts.Token);

void OnSessionEstablished(RakNetSession session)
{
    Console.WriteLine("RakNet session established to Rcon server.");
    session.PacketConstructor += OnPacketParse;
    session.PacketReceived += OnPacketReceived;
    session.SessionDisconnected += OnSessionDisconnected;

    operationalSession = session;
    Interlocked.Exchange(ref activityHeartbeatCounter, 1);
}

void OnSessionDisconnected(RakNetSession session)
{
    applicationShutdownCts.Cancel();

    if (!isCommandExecuted)
    {
        var errorColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Connection pipeline failure: Received unexpected DisconnectionNotification before console command execution finished.");
        }
        finally
        {
            Console.ForegroundColor = errorColor;
        }
        Environment.Exit(1);
    }

    Console.WriteLine("Closed connection to Rcon server successfully.");
    Environment.Exit(0);
}

void OnPacketReceived(RakNetSession session, IPacket packet)
{
    Interlocked.Exchange(ref activityHeartbeatCounter, 1);

    switch (packet)
    {
        case DosUnknown87:
            break;

        case DosEnumerationList:
            if (!isCommandExecuted)
            {
                Console.WriteLine("Sending console command payload parameters:");
                Console.WriteLine($"> {targetCommand} {string.Join(" ", commandArguments)}");

                var consoleCmd = new DosSendConsoleCommand
                {
                    Command = targetCommand,
                    Arguments = commandArguments
                };
                session.SendEncapsulated(consoleCmd, EncapsulatedReliability.ReliableOrdered);
            }
            break;

        case DosConsoleResponse consoleResponse:
            bool demandsImmediateDisconnect = false;
            var terminalColor = Console.ForegroundColor;

            try
            {
                foreach (var line in consoleResponse.Lines)
                {
                    Console.ForegroundColor = line.Level switch
                    {
                        4 => ConsoleColor.Green,
                        5 => ConsoleColor.Red,
                        _ => terminalColor
                    };

                    if (line.Level is 4 or 5)
                    {
                        demandsImmediateDisconnect = true;
                    }

                    Console.WriteLine(line.Line);
                }
            }
            finally
            {
                Console.ForegroundColor = terminalColor;
            }

            isCommandExecuted = true;

            if (demandsImmediateDisconnect && operationalSession is not null)
            {
                var disconnectCmd = new DosDisconnectConsole();
                operationalSession.SendEncapsulated(disconnectCmd, EncapsulatedReliability.ReliableOrdered);
            }
            break;

        default:
            throw new NotSupportedException($"Incoming raw encapsulated packet mapping configuration is unhandled: {packet.GetType().Name}");
    }
}

static IPacket? OnPacketParse(byte id) => (DosPacketId)id switch
{
    DosPacketId.DosUnknown87 => new DosUnknown87(),
    DosPacketId.DosSendConsoleCommand => new DosSendConsoleCommand(),
    DosPacketId.DosDisconnectConsole => new DosDisconnectConsole(),
    DosPacketId.DosConsoleResponse => new DosConsoleResponse(),
    DosPacketId.DosEnumerationList => new DosEnumerationList(),
    _ => null
};