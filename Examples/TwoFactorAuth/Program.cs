using static Logger<Client>;

// start the server, connect the client, authenticate
using var server = Server.Start();
using var client = Client.Start();

WriteLine("Program: press Enter to quit.");
ReadLine();

