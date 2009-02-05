// Copyright 2009 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Diagnostics;
using System.Collections.Generic;
using NDesk.DBus;
using NDesk.DBus.Authentication;
using NDesk.DBus.Transports;
using org.freedesktop.DBus;

using System.IO;
using System.Net;
using System.Net.Sockets;

using System.Reflection;

using System.Threading;

public class DBusDaemon
{
	public static void Main (string[] args)
	{
		bool isServer = true;
		string addr = "tcp:host=localhost,port=12345";

		if (args.Length >= 1) {

			if (args[0] == "client")
				isServer = false;

			if (args.Length >= 2)
				addr = args[1];
		}

		if (isServer)
			RunServer (addr);
		else
			RunClient (addr);
		//Console.Error.WriteLine ("Usage: test-server-tcp [server PORT|client HOSTNAME PORT]");
	}

	static void RunServer (string addr)
	{
		/*
		int port;
		string hostname = "127.0.0.1";
		//IPAddress ipaddr = IPAddress.Parse ("127.0.0.1");

		port = 12345;
		string addr = "tcp:host=localhost,port=" + port;
		*/

		//port = Int32.Parse (args[1]);
		TcpServer serv = new TcpServer (addr);

		ServerBus sbus = new ServerBus ();
		sbus.server = serv;
		serv.SBus = sbus;
		serv.NewConnection += sbus.AddConnection;

		//serv.Listen ();

		new Thread (new ThreadStart (serv.Listen)).Start ();

		//GLib.Idle.Add (delegate { serv.Listen (); return false; });

		GLib.MainLoop main = new GLib.MainLoop ();
		main.Run ();
	}

	static void RunClient (string addr)
	{
		//ObjectPath myOpath = new ObjectPath ("/org/ndesk/test");
		//string myNameReq = "org.ndesk.test";

		/*
		SocketTransport transport = new SocketTransport ();
		transport.Open (hostname, port);
		conn = new Connection (transport);
		*/

		Console.WriteLine ("Opening " + addr);
		Bus bus = Bus.Open (addr);

		Console.WriteLine (bus.UniqueName);
		Console.WriteLine (bus.RequestName ("org.ndesk.Test"));

		IBus ibus = bus.GetObject<IBus> ("org.freedesktop.DBus", new ObjectPath ("/org/freedesktop/DBus"));

		string[] names = ibus.ListNames ();
		Console.WriteLine (String.Join (" ", names));

		Console.WriteLine (bus.RequestName ("org.ndesk.Test"));

		ibus.AddMatch ("type='method_call'");
		ibus.AddMatch ("type='method_call'");
		ibus.AddMatch ("type='method_call'");
		ibus.RemoveMatch ("type='method_call'");
		ibus.RemoveMatch ("type='method_call'");
		ibus.RemoveMatch ("type='method_call'");

		/*
		DemoObject demo = conn.GetObject<DemoObject> (myNameReq, myOpath);
		demo.GiveNoReply ();
		//float ret = demo.Hello ("hi from test client", 21);
		float ret = 200;
		while (ret > 5) {
			ret = demo.Hello ("hi from test client", (int)ret);
			Console.WriteLine ("Returned float: " + ret);
			System.Threading.Thread.Sleep (1000);
		}
		*/
	}

}

	public class ServerBus : org.freedesktop.DBus.IBus
	{
		static string ValidateBusName (string name)
		{
			if (name == String.Empty)
				return "cannot be empty";
			//if (name.StartsWith (":"))
			//	return "cannot be a unique name";
			return null;
		}

		static bool BusNameIsValid (string name, out string nameError)
		{
			nameError = ValidateBusName (name);
			return nameError == null;
		}

		readonly List<Connection> conns = new List<Connection> ();

		public static readonly ObjectPath Path = new ObjectPath ("/org/freedesktop/DBus");
		//static ObjectPath Path2 = new ObjectPath ("/");
		const string DBusBusName = "org.freedesktop.DBus";

		//internal Server server;
		internal TcpServer server;
		//Connection Caller
		ServerConnection Caller
		{
			get {
				//return server.CurrentMessageConnection;
				return server.CurrentMessageConnection as ServerConnection;
			}
		}

		// TODO: Should be the : name, or "(inactive)" / caller.UniqueName
		//string callerUniqueName = ":?";

		public void AddConnection (Connection conn)
		{
			Console.Error.WriteLine ("AddConn");

			if (conns.Contains (conn))
				throw new Exception ("Cannot add connection");

			conns.Add (conn);
			conn.Register (Path, this);
			//conn.Register (Path2, this);
		}

		public void RemoveConnection (Connection conn)
		{
			Console.Error.WriteLine ("RemoveConn");

			if (!conns.Remove (conn))
				throw new Exception ("Cannot remove connection");

			//conn.Unregister (Path);
			//conn.Unregister (Path2);

			List<string> namesToDisown = new List<string> ();
			foreach (KeyValuePair<string,Connection> pair in Names) {
				if (pair.Value == conn)
					namesToDisown.Add (pair.Key);
			}

			List<MatchRule> toRemove = new List<MatchRule> ();
			foreach (KeyValuePair<MatchRule,List<Connection>> pair in Rules) {
				while (pair.Value.Remove (Caller)) { }
				//while (pair.Value.Remove (Caller)) { Console.WriteLine ("Remove!"); }
				//pair.Value.RemoveAll ( delegate (Connection conn) { conn == Caller; } )
				if (pair.Value.Count == 0)
					toRemove.Add (pair.Key);
					//Rules.Remove (pair);
					//Rules.Remove<KeyValuePair<MatchRule,List<Connection>>> (pair);
					//((ICollection<System.Collections.Generic.KeyValuePair<MatchRule,List<Connection>>>)Rules).Remove<KeyValuePair<MatchRule,List<Connection>>> (pair);
					//((ICollection<System.Collections.Generic.KeyValuePair<MatchRule,List<Connection>>>)Rules).Remove (pair);
			}

			foreach (MatchRule r in toRemove)
				Rules.Remove (r);

			// TODO: Check the order of signals
			// TODO: Atomicity

			foreach (string name in namesToDisown)
				Names.Remove (name);

			foreach (string name in namesToDisown)
				NameOwnerChanged (name, Caller.UniqueName, String.Empty);

			//NameOwnerChanged (Caller.UniqueName, Caller.UniqueName, String.Empty);

			// FIXME: Unregister earlier?
			conn.Unregister (Path);
			//conn.Unregister (Path2);
		}

		//SortedList<>
		readonly Dictionary<string,Connection> Names = new Dictionary<string,Connection> ();
		//readonly SortedList<string,Connection> Names = new SortedList<string,Connection> ();
		//readonly SortedDictionary<string,Connection> Names = new SortedDictionary<string,Connection> ();
		public RequestNameReply RequestName (string name, NameFlag flags)
		{
			string nameError;
			if (!BusNameIsValid (name, out nameError))
				throw new ArgumentException (String.Format ("Requested name \"{0}\" is not valid: {1}", name, nameError), "name");

			if (name.StartsWith (":"))
				throw new ArgumentException (String.Format ("Cannot acquire a name starting with ':' such as \"{0}\"", name), "name");

			if (name == DBusBusName)
				throw new ArgumentException (String.Format ("Connection \"{0}\" is not allowed to own the name \"{1}\" because it is reserved for D-Bus' use only", Caller.UniqueName, name), "name");

			// TODO: Policy delegate support

			// TODO: NameFlag support

			Connection c;
			if (!Names.TryGetValue (name, out c)) {
				Names[name] = Caller;
				// NameAcquired should only be sent to the caller?
				//NameAcquired (name);
				NameOwnerChanged (name, String.Empty, Caller.UniqueName);
				return RequestNameReply.PrimaryOwner;
			} else if (c == Caller)
				return RequestNameReply.AlreadyOwner;
			else
				return RequestNameReply.Exists;
		}

		public ReleaseNameReply ReleaseName (string name)
		{
			// TODO: Check for : name here?

			Connection c;
			if (!Names.TryGetValue (name, out c))
				return ReleaseNameReply.NonExistent;

			if (c != Caller)
				return ReleaseNameReply.NotOwner;

			Names.Remove (name);
			NameOwnerChanged (name, Caller.UniqueName, String.Empty);
			return ReleaseNameReply.Released;
		}

		/*
		public string MyMeth (uint val1, string val2)
		{
			Console.WriteLine ("MyMeth " + val1 + " " + val2);
			return "WEE!";
		}
		*/

		/*
		public struct MethData
		{
			public int A;
			public int B;
			public int C;
		}
		*/

		public class MethDataBase
		{
			public int A;
		}

		public class MethData : MethDataBase
		{
			public int B;
			public int C;
			//public MethData[] Children;
			public long[] Children;
		}

		public void MyMeth0 ()
		{
		}

		public string MyMeth (MethData d, int[] ary, IDictionary<int,string> dict)
		{
			Console.WriteLine ("MyMeth struct " + d.A + " " + d.B + " " + d.C);
			foreach (int i in ary)
				Console.WriteLine (i);
			Console.WriteLine ("Children: " + d.Children.Length);
			Console.WriteLine ("Dict entries: " + dict.Count);
			Console.WriteLine ("321: " + dict[321]);
			return "WEE!";
		}

		readonly long uniqueBase = 1;
		long uniqueNames = 1;
		public string Hello ()
		{
			// org.freedesktop.DBus.Error.Failed: Already handled an Hello message
			if (Caller.UniqueName != null)
				throw new Exception ("Already handled an Hello message");

			Console.Error.WriteLine ("Hello!");
			//return ":1";
			string uniqueName = String.Format (":{0}.{1}", uniqueBase, uniqueNames++);

			Caller.UniqueName = uniqueName;
			Names[uniqueName] = Caller;

			// These signals ought to be queued up and send after the reply is sent?
			// TODO: NameAcquired should only be sent to the caller
			// Should have the Destination field set!
			NameAcquired (uniqueName);
			NameOwnerChanged (uniqueName, String.Empty, uniqueName);

			return uniqueName;
		}

		public string[] ListNames ()
		{
			//return Names.Keys.ToArray ();
			//List<string> names = new List<string> (Names.Keys);
			List<string> names = new List<string> ();
			names.Add (DBusBusName);
			names.AddRange (Names.Keys);
			return names.ToArray ();
		}

		public string[] ListActivatableNames ()
		{
			return new string[0];
		}

		public bool NameHasOwner (string name)
		{
			if (name == DBusBusName)
				return true;

			return Names.ContainsKey (name);
		}

		public event NameOwnerChangedHandler NameOwnerChanged;
		public event NameLostHandler NameLost;
		public event NameAcquiredHandler NameAcquired;

		public StartReply StartServiceByName (string name, uint flags)
		{
			if (name == DBusBusName)
				return StartReply.AlreadyRunning;

			if (Names.ContainsKey (name))
				return StartReply.AlreadyRunning;

			StartProcessNamed (name);
			return StartReply.Success;

			//return StartReply.Success;
			//throw new NotSupportedException ();
		}

		public string GetNameOwner (string name)
		{
			if (name == DBusBusName)
				return DBusBusName;

			Connection c;
			if (!Names.TryGetValue (name, out c))
				throw new Exception (String.Format ("Could not get owner of name '{0}': no such name", name));

			return ((ServerConnection)c).UniqueName;
		}

		//public uint GetConnectionUnixUser (string connection_name)
		public uint GetConnectionUnixUser (string name)
		{
			//if (name == DBusBusName)
			//	return 0;

			Connection c;
			if (!Names.TryGetValue (name, out c))
				throw new Exception ();

			return (uint)((ServerConnection)c).UserId;
		}

		internal void HandleMessage (Message msg)
		{
			if (msg == null)
				return;

			//if (msg.Signature == new Signature ("u"))
			if (false) {
				System.Reflection.MethodInfo target = typeof (ServerBus).GetMethod ("MyMeth");
				//System.Reflection.MethodInfo target = typeof (ServerBus).GetMethod ("MyMeth0");
				Signature inSig, outSig;
				TypeImplementer.SigsForMethod (target, out inSig, out outSig);
				Console.WriteLine ("inSig: " + inSig);

				if (msg.Signature == inSig) {
					MethodCaller caller = TypeImplementer.GenCaller (target, this);
					//caller (new MessageReader (msg), msg);

					MessageWriter retWriter = new MessageWriter ();
					caller (new MessageReader (msg), msg, retWriter);

					if (msg.ReplyExpected) {
						MethodReturn method_return = new MethodReturn (msg.Header.Serial);
						Message replyMsg = method_return.message;
						replyMsg.Body = retWriter.ToArray ();
						Console.WriteLine ("replyMsg body: " + replyMsg.Body.Length);
						/*
						try {
						replyMsg.Header.Fields[FieldCode.Destination] = msg.Header.Fields[FieldCode.Sender];
						replyMsg.Header.Fields[FieldCode.Sender] = Caller.UniqueName;
						} catch (Exception e) {
							Console.Error.WriteLine (e);
						}
						*/
						replyMsg.Header.Fields[FieldCode.Destination] = Caller.UniqueName;
						replyMsg.Header.Fields[FieldCode.Sender] = "org.freedesktop.DBus";
						replyMsg.Signature = outSig;
						{
							Caller.Send (replyMsg);

							/*
							replyMsg.Header.Serial = Caller.GenerateSerial ();
							MessageDumper.WriteMessage (replyMsg, Console.Out);
							Caller.WriteMessageReal (replyMsg);
							*/
							return;
						}
					}
				}
			}

			//List<Connection> recipients = new List<Connection> ();
			HashSet<Connection> recipients = new HashSet<Connection> ();
			//HashSet<Connection> recipientsAll = new HashSet<Connection> (Connections);

			object fieldValue;
			if (msg.Header.Fields.TryGetValue (FieldCode.Destination, out fieldValue)) {
				string destination = (string)fieldValue;
				Connection destConn;
				if (Names.TryGetValue (destination, out destConn))
					recipients.Add (destConn);

				// Send an error when there's no hope of getting the requested reply
				else if (destination != "org.freedesktop.DBus" && msg.ReplyExpected) {
					// Error org.freedesktop.DBus.Error.ServiceUnknown: The name {0} was not provided by any .service files
					StartProcessNamed (destination);
					Message rmsg = MessageHelper.CreateUnknownMethodError (new MethodCall (msg));
					if (rmsg != null) {
						rmsg.Header.Serial = Caller.GenerateSerial ();
						//Caller.Send (rmsg);
						Caller.WriteMessageReal (rmsg);
						return;
					}
				}
			}

			HashSet<Connection> recipientsMatchingHeader = new HashSet<Connection> ();

			HashSet<ArgMatchTest> a = new HashSet<ArgMatchTest> ();
			foreach (KeyValuePair<MatchRule,List<Connection>> pair in Rules) {
				if (recipients.IsSupersetOf (pair.Value))
					continue;
				if (pair.Key.MatchesHeader (msg)) {
					a.UnionWith (pair.Key.Args);
					recipientsMatchingHeader.UnionWith (pair.Value);
				}
			}

			MatchRule.Test (a, msg);

			foreach (KeyValuePair<MatchRule,List<Connection>> pair in Rules) {
				if (recipients.IsSupersetOf (pair.Value))
					continue;
				if (!recipientsMatchingHeader.IsSupersetOf (pair.Value))
					continue;
				if (a.IsSupersetOf (pair.Key.Args))
					recipients.UnionWith (pair.Value);
			}

			foreach (Connection conn in recipients) {
				// TODO: rewrite/don't header fields
				//conn.WriteMessage (msg);
				((ServerConnection)conn).WriteMessageReal (msg);
			}
		}

		//SortedDictionary<MatchRule,int> Rules = new SortedDictionary<MatchRule,int> ();
		//Dictionary<MatchRule,int> Rules = new Dictionary<MatchRule,int> ();
		Dictionary<MatchRule,List<Connection>> Rules = new Dictionary<MatchRule,List<Connection>> ();
		public void AddMatch (string rule)
		{
			MatchRule r = MatchRule.Parse (rule);

			if (r == null)
				throw new Exception ("r == null");

			if (!Rules.ContainsKey (r))
				Rules[r] = new List<Connection> ();

			// Each occurrence of a Connection in the list represents one value-unique AddMatch call
			Rules[r].Add (Caller);

			Console.WriteLine ("Added. Rules count: " + Rules.Count);
		}

		public void RemoveMatch (string rule)
		{
			MatchRule r = MatchRule.Parse (rule);

			if (r == null)
				throw new Exception ("r == null");

			if (!Rules.ContainsKey (r))
				throw new Exception ();

			// We remove precisely one occurrence of the calling connection
			Rules[r].Remove (Caller);
			if (Rules[r].Count == 0)
				Rules.Remove (r);

			Console.WriteLine ("Removed. Rules count: " + Rules.Count);
		}

		public string GetId ()
		{
			return Caller.Id.ToString ();
		}

		// Undocumented in spec
		public string[] ListQueuedOwners (string name)
		{
			// ?
			if (name == DBusBusName)
				return new string[] { DBusBusName };

			Connection c;
			if (!Names.TryGetValue (name, out c))
				throw new Exception (String.Format ("Could not get owners of name '{0}': no such name", name));

			return new string[] { ((ServerConnection)c).UniqueName };
			//throw new NotImplementedException ();
		}

		// Undocumented in spec
		public uint GetConnectionUnixProcessID (string connection_name)
		{
			return 0;
			//throw new NotImplementedException ();
		}

		// Undocumented in spec
		public byte[] GetConnectionSELinuxSecurityContext (string connection_name)
		{
			throw new Exception (String.Format ("org.freedesktop.DBus.Error.SELinuxSecurityContextUnknown: Could not determine security context for '{0}'", connection_name));
		}

		// Undocumented in spec
		public void ReloadConfig ()
		{
		}

		void StartProcessNamed (string name)
		{
			Console.WriteLine ("Start " + name);
			return;
			try {
			string fname = String.Format ("/usr/share/dbus-1/services/{0}.service", name);
			using (TextReader r = new StreamReader (fname)) {
				string ln;
				while ((ln = r.ReadLine ()) != null) {
					if (ln.StartsWith ("Exec=")) {
						string bin = ln.Remove (0, 5);
						StartProcess (bin);
					}
				}
			}
			} catch (Exception e) {
				Console.Error.WriteLine (e);
			}

		}

		void StartProcess (string fname)
		{
			return;
			try {
			 ProcessStartInfo startInfo = new ProcessStartInfo (fname);
			 startInfo.UseShellExecute = false;
			 startInfo.EnvironmentVariables["DBUS_STARTER_BUS_TYPE"] = "session";
			 startInfo.EnvironmentVariables["DBUS_SESSION_BUS_ADDRESS"] = server.address;
			 startInfo.EnvironmentVariables["DBUS_STARTER_ADDRESS"] = server.address;
			 startInfo.EnvironmentVariables["DBUS_STARTER_BUS_TYPE"] = "session";
			 Process myProcess = Process.Start (startInfo);
			} catch (Exception e) {
				Console.Error.WriteLine (e);
			}
		}
	}

class ServerConnection : Connection
{
	//public Server Server;
	public TcpServer Server;

	public ServerConnection (Transport t) : base (t)
	{
	}

	bool shouldDump = false;
	//bool shouldDump = true;

	//bool isHelloed = false;
	//bool isConnected = true;
	override internal void HandleMessage (Message msg)
	{
		//Console.Error.WriteLine ("Message!");

		Server.CurrentMessageConnection = this;
		Server.CurrentMessage = msg;

		if (!isConnected)
			return;

		if (msg == null) {
			Console.Error.WriteLine ("Disconnected!");
			isConnected = false;
			//Server.Bus.RemoveConnection (this);
			//ServerBus sbus = Unregister (new ObjectPath ("/org/freedesktop/DBus")) as ServerBus;

			/*
			ServerBus sbus = Unregister (new ObjectPath ("/org/freedesktop/DBus")) as ServerBus;
			Register (new ObjectPath ("/org/freedesktop/DBus"), sbus);
			sbus.RemoveConnection (this);
			*/

			Server.SBus.RemoveConnection (this);

			//Server.ConnectionLost (this);
			return;
		}

		if (shouldDump) {
			MessageDumper.WriteComment ("Handling:", Console.Out);
			MessageDumper.WriteMessage (msg, Console.Out);
		}

		if (UniqueName != null)
			msg.Header.Fields[FieldCode.Sender] = UniqueName;

		object fieldValue;
		if (msg.Header.Fields.TryGetValue (FieldCode.Destination, out fieldValue)) {
			if ((string)fieldValue == "org.freedesktop.DBus") {

				// Workaround for our daemon only listening on a single path
				if (msg.Header.MessageType == NDesk.DBus.MessageType.MethodCall)
					msg.Header.Fields[FieldCode.Path] = ServerBus.Path;

				base.HandleMessage (msg);
				//return;
			}
		}
		//base.HandleMessage (msg);

		Server.SBus.HandleMessage (msg);

		// TODO: we ought to make sure these are cleared in other cases above too!
		Server.CurrentMessageConnection = null;
		Server.CurrentMessage = null;
	}

	override internal void WriteMessage (Message msg)
	{
		if (!isConnected)
			return;

		if (msg.Header.MessageType != NDesk.DBus.MessageType.MethodReturn) {
			msg.Header.Fields[FieldCode.Sender] = "org.freedesktop.DBus";
		}

		if (UniqueName != null)
			msg.Header.Fields[FieldCode.Destination] = UniqueName;

		if (shouldDump) {
			MessageDumper.WriteComment ("Sending:", Console.Out);
			MessageDumper.WriteMessage (msg, Console.Out);
		}

		//base.WriteMessage (msg);
		WriteMessageReal (msg);
	}

	internal void WriteMessageReal (Message msg)
	{
		try {
			base.WriteMessage (msg);
		} catch (System.IO.IOException) {
			isConnected = false;
		}
	}

	public string UniqueName = null;
	public long UserId = 0;

	~ServerConnection ()
	{
		Console.Error.WriteLine ("Good! ~ServerConnection () for {0}", UniqueName);
	}
}

internal class BusContext
{
	protected Connection connection = null;
	public Connection Connection
	{
		get {
			return connection;
		}
	}

	protected Message message = null;
	internal Message CurrentMessage
	{
		get {
			return message;
		}
	}

	public string SenderName = null;
}

class TcpServer : Server
{
	uint port = 0;
	public TcpServer (string address)
	{
		AddressEntry[] entries = Address.Parse (address);
		AddressEntry entry = entries[0];

		if (entry.Method != "tcp")
			throw new Exception ();

		this.address = entry.ToString ();

		Id = entry.GUID;
		if (Id == UUID.Zero)
			Id = UUID.Generate ();

		string val;
		if (entry.Properties.TryGetValue ("port", out val))
			port = UInt32.Parse (val);
	}

	public override void Disconnect ()
	{
	}

	public void Listen ()
	{
		TcpListener server = new TcpListener (IPAddress.Any, (int)port);
		server.Server.Blocking = true;

		server.Start ();

		while (true) {
			Console.WriteLine ("Waiting for client on " + port);
			TcpClient client = server.AcceptTcpClient ();
			Console.WriteLine ("Client accepted");

			//TODO: use the right abstraction here, probably using the Server class
			SocketTransport transport = new SocketTransport ();
			client.Client.Blocking = true;
			transport.SocketHandle = (long)client.Client.Handle;
			transport.Stream = client.GetStream ();
			//Connection conn = new Connection (transport);
			//Connection conn = new ServerConnection (transport);
			ServerConnection conn = new ServerConnection (transport);
			conn.Server = this;
			conn.Id = Id;

			if (conn.Transport.Stream.ReadByte () != 0)
				return;

			SaslPeer remote = new SaslPeer ();
			remote.stream = transport.Stream;
			SaslServer local = new SaslServer ();
			local.stream = transport.Stream;
			local.Guid = Id;

			local.Peer = remote;
			remote.Peer = local;

			bool success = local.Authenticate ();

			Console.WriteLine ("Success? " + success);

			if (!success)
				return;

			conn.UserId = ((SaslServer)local).uid;

			//GLib.Idle.Add (delegate {

			if (NewConnection != null)
				NewConnection (conn);

			//BusG.Init (conn);
			/*
			conn.Iterate ();
			Console.WriteLine ("done iter");
			BusG.Init (conn);
			Console.WriteLine ("done init");
			*/

			//GLib.Idle.Add (delegate { BusG.Init (conn); return false; });
			BusG.Init (conn);
			Console.WriteLine ("done init");

			//return false;
			//});
		}
	}

	// TODO: Make these a thread-specific CallContext prop
	public Connection CurrentMessageConnection;
	public Message CurrentMessage;

	public ServerBus SBus = null;

	/*
	public void ConnectionLost (Connection conn)
	{
	}
	*/

	public override event Action<Connection> NewConnection;
}
