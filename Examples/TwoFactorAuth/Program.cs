using static Logger<Program>;

// start the server, connect the client, authenticate
using var server = Server.Start();
using var client = Client.Start();

WriteLine("Press Enter to quit.");
ReadLine();

