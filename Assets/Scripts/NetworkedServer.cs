using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    LinkedList<PlayerAccount> playerAccounts;
    LinkedList<PlayerMove> playerMoves;
    private int moveIndex = 0;

    LinkedList<GameRoom> gameRooms;

    const int playerAccountNameAndPassword = 1;

    string playerAccountDataPath;
    string playersMovesDataPath;

    int playerWaitingForMatchWithID = -1;

    int playerJoiningAsObserverID = -1;
    int observerReferenceID = -1;

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccountDataPath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        playersMovesDataPath = Application.dataPath + Path.DirectorySeparatorChar + "MoveList.txt";

        playerAccounts = new LinkedList<PlayerAccount>();
        playerMoves = new LinkedList<PlayerMove>();
        gameRooms = new LinkedList<GameRoom>();

        LoadPlayerAccount();
    }

    // Update is called once per frame
    void Update()
    {
        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }
    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');

        int signifier = int.Parse(csv[0]);


        //Create Account
        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            Debug.Log("Create Account");

            string n = csv[1];
            string p = csv[2];
            bool nameInUse = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameInUse = true;
                    break;
                }
            }

            if (nameInUse)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "", id);
            }
            else
            {
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);
                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "", id);

                SavePlayerAccount();
            }

            //If not, create new account, add to list and save to list
            //Send Client success or failure
        }

        //Login to Account
        else if (signifier == ClientToServerSignifiers.Login)
        {
            Debug.Log("Login to Account");

            string n = csv[1];
            string p = csv[2];

            bool hasNameBeenFound = false;
            bool hasMsgBeenSentToClient = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    hasNameBeenFound = true;

                    if (pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + "", id);
                        hasMsgBeenSentToClient = true;
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                        hasMsgBeenSentToClient = true;
                    }
                }
                else
                {
                    //?
                }
            }

            if (!hasNameBeenFound)
            {
                if (!hasMsgBeenSentToClient)
                {
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                }
            }

            //Check if player account already exists
            //Send client success/failure

        }

        //Join Queue for GameRoom
        else if (signifier == ClientToServerSignifiers.JoinQueueForGameRoom)
        {
            //Observer Check
            //GameRoom gameRoomAsObserver = null;

            if (playerJoiningAsObserverID == 0)
            {
                Debug.Log("Observer");
                GameRoom gr = GetGameRoomWithClientID(observerReferenceID);

                gr.AddObserver(id);

                SendMessageToClient(ServerToClientSignifiers.JoinAsObserver + "", id);
            }


            //**Smart Check**
            if (playerWaitingForMatchWithID == -1)
            {
                playerWaitingForMatchWithID = id;
            }
            else
            {
                GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);

                gameRooms.AddLast(gr);

                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.playerID2);

                playerJoiningAsObserverID = 0;
                observerReferenceID = id;

                playerWaitingForMatchWithID = -1;
            }
        }

        //Play Game
        else if (signifier == ClientToServerSignifiers.PlayGame)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.FirstPlayerSet + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifiers.PlayersTurn + "", gr.playerID1);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.SecondPlayerSet + "", gr.playerID2);
                }
            }
        }

        //Turn Taken
        else if (signifier == ClientToServerSignifiers.TurnTaken)
        {
            string node = csv[1];

            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    //Player 1 Always X's
                    SendMessageToClient(ServerToClientSignifiers.PlayersTurn + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifiers.OpponentNode + "," + node, gr.playerID2);

                    SendMessageToClient(ServerToClientSignifiers.UpdateObservers + "," + node + "," + "1", gr.observerID1);


                    PlayerMove move = new PlayerMove(gr.playerID1, int.Parse(node), 1);
                    playerMoves.AddLast(move);
                }
                else if (gr.playerID2 == id)
                {
                    //Player 2 Always O's
                    SendMessageToClient(ServerToClientSignifiers.PlayersTurn + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifiers.OpponentNode + "," + node, gr.playerID1);

                    SendMessageToClient(ServerToClientSignifiers.UpdateObservers + "," + node + "," + "2", gr.observerID1);

                    PlayerMove move = new PlayerMove(gr.playerID2, int.Parse(node), 2);
                    playerMoves.AddLast(move);
                }
            }
        }

        else if (signifier == ClientToServerSignifiers.PlayerWin)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.WinConditionForPlayer + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifiers.LoseConditionForPlayer + "", gr.playerID2);
                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.WinConditionForPlayer + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifiers.LoseConditionForPlayer + "", gr.playerID1);
                }
            }
        }

        else if (signifier == ClientToServerSignifiers.PlayerMessage)
        {
            string playerMsg = csv[1];

            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.DisplayPlayerMessage + "," + playerMsg , gr.playerID1);
                    SendMessageToClient(ServerToClientSignifiers.DisplayOpponentMessage + "," + playerMsg, gr.playerID2);
                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.DisplayPlayerMessage + "," + playerMsg, gr.playerID2);
                    SendMessageToClient(ServerToClientSignifiers.DisplayOpponentMessage + "," + playerMsg, gr.playerID1);
                }
            }
        }

        else if (signifier == ClientToServerSignifiers.RequestReplayMove)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    foreach (PlayerMove m in playerMoves)
                    {
                        SendMessageToClient(ServerToClientSignifiers.ReplayMove + "," + m.playerID + "," + m.nodeID + "," + m.nodeSig, gr.playerID1);
                    }
                }
                else if (gr.playerID2 == id)
                {
                    foreach (PlayerMove m in playerMoves)
                    {
                        SendMessageToClient(ServerToClientSignifiers.ReplayMove + "," + m.playerID + "," + m.nodeID + "," + m.nodeSig, gr.playerID2);
                    }
                }
            }
        }
    }


    /*
    private void SavePlayerMove()
    {
        StreamWriter sw = new StreamWriter(playersMovesDataPath);

        foreach (PlayerMove m in playerMoves)
        {
            sw.WriteLine()
        }
    }
    */

    private void SavePlayerAccount()
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt");

        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(playerAccountNameAndPassword + "," + pa.name + "," + pa.password);
        }
        sw.Close();
    }

    private void LoadPlayerAccount()
    {
        if (File.Exists(playerAccountDataPath))
        {
            StreamReader sr = new StreamReader(playerAccountDataPath);

            string line;

            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);

                if (signifier == playerAccountNameAndPassword)
                {
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                }
            }
            sr.Close();
        }
    }

    
    private GameRoom GetGameRoomWithClientID(int id)
    {
        foreach (GameRoom gr in gameRooms)
        {
            if (gr.playerID1 == id || gr.playerID2 == id)
            {
                return gr;
            }
        }
        return null;
    }
}



public class PlayerMove
{
    public int playerID;
    public int nodeID;
    public int nodeSig;

    public PlayerMove(int PlayerID, int NodeID, int NodeSig)
    {
        playerID = PlayerID;
        nodeID = NodeID;
        nodeID = NodeSig;
    }
}


public class PlayerAccount
{
    public string name;
    public string password;

    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}


public class GameRoom
{
    public int playerID1;
    public int playerID2;

    public int observerID1;

    LinkedList<PlayerAccount> observerList;

    public bool canHaveObserver;

    public GameRoom(int PlayerID1, int PlayerID2)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
        canHaveObserver = true;
        observerList = new LinkedList<PlayerAccount>();
    }

    public void AddObserver(int observerID)
    {
        observerID1 = observerID;
    }
}



public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;

    public const int Login = 2;

    public const int JoinQueueForGameRoom = 3;

    public const int PlayGame = 4;

    public const int TurnTaken = 5;

    public const int PlayerWin = 6;

    public const int PlayerMessage = 7;

    public const int RequestReplayMove = 8;
}


public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;

    public const int LoginFailed = 2;

    public const int AccountCreationComplete = 3;

    public const int AccountCreationFailed = 4;

    public const int GameStart = 5;

    //Addition

    public const int FirstPlayerSet = 6;

    public const int SecondPlayerSet = 7;

    public const int PlayersTurn = 8;

    public const int OpponentNode = 9;

    public const int WinConditionForPlayer = 10;

    public const int LoseConditionForPlayer = 11;

    public const int DisplayPlayerMessage = 12;

    public const int DisplayOpponentMessage = 13;

    public const int OpponentPlayed = 14;

    public const int JoinAsObserver = 15;

    public const int UpdateObservers = 16;

    public const int ReplayMove = 17;
}

