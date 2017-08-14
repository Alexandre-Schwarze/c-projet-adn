﻿using NodeNet.Data;
using NodeNet.Map_Reduce;
using NodeNet.Network.Nodes;
using NodeNet.Network.States;
using NodeNet.Tasks;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NodeNet.Network.Orch
{
    public abstract class Orchestrator : Node, IOrchestrator
    {
        #region Properties
        private List<Tuple<int, Node>> UnidentifiedNodes;
        /* Nombre de noeuds connectés */
        private int nbNodes = 0;
       
              
        // Task monitoring nodes */
        Tuple<int, NodeState> MonitorTask;

        /* Liste des noeuds connectés */
        private ObservableCollection<Node> nodes;
        public ObservableCollection<Node> Nodes
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get { return nodes; }
            [MethodImpl(MethodImplOptions.Synchronized)]
            set { nodes = value; }
        }

        /* Liste des clients connectés */
        private List<Node> clients;
        public List<Node> Clients
        {
            get { return clients; }
            set { clients = value; }
        }

        /* Liste des noeuds connectés */
        private List<Tuple<int,List<int>,bool>> taskDistrib;
        public List<Tuple<int, List<int>, bool>> TaskDistrib
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get { return taskDistrib; }
            [MethodImpl(MethodImplOptions.Synchronized)]
            set { taskDistrib = value; }
        }

        #endregion

        public Orchestrator(string name, string address, int port) : base(name, address, port)
        {
            UnidentifiedNodes = new List<Tuple<int, Node>>();
            Nodes = new ObservableCollection<Node>();
            Clients = new List<Node>();
            TaskDistrib = new List<Tuple<int, List<int>,bool>>();
            WorkerFactory.AddWorker(IDENT_METHOD, new TaskExecutor(this,IdentNode,null,null));
            WorkerFactory.AddWorker(GET_CPU_METHOD, new TaskExecutor(this,ProcessCPUStateOrder,null,null));
            WorkerFactory.AddWorker(TASK_STATUS_METHOD, new TaskExecutor(this, RefreshTaskState, null, null));
        }

        #region Inherited methods

        public async void Listen()
        {
            TcpListener listener = new TcpListener(IPAddress.Parse(Address), Port);
            listener.Start();
            Console.WriteLine("Server is listening on port : " + Port);
            while (true)
            {
                Socket sock = await listener.AcceptSocketAsync();
                DefaultNode connectedNode = new DefaultNode("", ((IPEndPoint)sock.RemoteEndPoint).Address + "", ((IPEndPoint)sock.RemoteEndPoint).Port, sock);
                nbNodes++;
                Console.WriteLine(String.Format("Client Connection accepted from {0}", sock.RemoteEndPoint.ToString()));
                GetIdentityOfNode(connectedNode);
                Receive(connectedNode);
            }
        }

        private void GetIdentityOfNode(DefaultNode connectedNode)
        {
            int tId = LastTaskID;
            Tuple<int, Node> taskNodeTuple = new Tuple<int, Node>(tId, connectedNode);
            UnidentifiedNodes.Add(taskNodeTuple);

            DataInput input = new DataInput()
            {
                Method = IDENT_METHOD,
                NodeGUID = NodeGUID,
                TaskId = tId,
                Data = new Tuple<String,int>(nbNodes.ToString(),connectedNode.Port),
                MsgType = MessageType.IDENT
            };
            SendData(connectedNode, input);
        }

        public new void Stop()
        {
            throw new NotImplementedException();
        }

        public void SendDataToAllNodes(DataInput input)
        {
            byte[] data = DataFormater.Serialize(input);
            Console.WriteLine("Send Data to " + Nodes.Count + " Node in orch Nodes list");
            /* Multi Client */
            foreach (Node node in Nodes)
            {
                try
                {
                    SendData(node, input);
                }
                catch (SocketException ex)
                {
                    /// Client Down ///
                    if (!node.NodeSocket.Connected)
                    {
                        Console.WriteLine("Client " + node.NodeSocket.RemoteEndPoint.ToString() + " Disconnected");
                    }
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        public override void ReceiveCallback(IAsyncResult ar)
        {
            Tuple<Node, byte[]> state = (Tuple<Node, byte[]>)ar.AsyncState;
            byte[] buffer = state.Item2;
            Node node = state.Item1;
            Socket client = node.NodeSocket;
            try
            {
                // Read data from the remote device.
                int bytesRead = client.EndReceive(ar);
                bytearrayList = new List<byte[]>();
                if (bytesRead == 4096)
                {
                    byte[] data = buffer;
                    bytearrayList.Add(data);
                }
                else
                {
                    DataInput input;
                    if (bytearrayList.Count > 0)
                    {
                        byte[] data = bytearrayList
                                     .SelectMany(a => a)
                                     .ToArray();
                        input = DataFormater.Deserialize<DataInput>(data);
                    }
                    else
                    {
                        input = DataFormater.Deserialize<DataInput>(buffer);
                    }


                    ProcessInput(input, node);
                    receiveDone.Set();
                }
                Receive(node);
            }
            catch (SocketException e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public override void ProcessInput(DataInput input, Node node)
        {
            TaskExecutor executor = WorkerFactory.GetWorker(input.Method);
            Object res = executor.DoWork(input);
            if(res != null) {
                DataInput resp = new DataInput()
                {
                    ClientGUID = input.ClientGUID,
                    NodeGUID = NodeGUID,
                    TaskId =input.TaskId,
                    Method = input.Method,
                    Data = res,
                    MsgType = MessageType.RESPONSE
                };
                SendData(node, resp);
            }
        }

        #endregion

        #region Task method

        public Object IdentNode(DataInput data)
        {
            Console.WriteLine("Process Ident On Orch");
            foreach (Tuple<int, Node> node in UnidentifiedNodes)
            {
                if (node.Item1 == data.TaskId)
                {
                    if (data.ClientGUID != null)
                    {
                        node.Item2.NodeGUID = data.ClientGUID;
                        Console.WriteLine("Add Client to list : " + node);
                        Clients.Add(node.Item2);
                        // TODO activer le monitoring pour ce client
                        SendNodesToClient(node.Item2);
                        startMonitoringForClient(node.Item2);
                    }
                    else if (data.NodeGUID != null)
                    {
                        node.Item2.NodeGUID = data.NodeGUID;
                        if (MonitorTask != null)
                        {
                            StartMonitoringForNode(data, node.Item2);
                        }
                        Console.WriteLine("Add Node to list : " + node);
                        Nodes.Add(node.Item2);
                        SendNodeToClients(node.Item2);
                    }
                }
            }
            return null;
        }

        private void startMonitoringForClient(Node client)
        {
            DataInput input = new DataInput()
            {
                Method = GET_CPU_METHOD,
                Data = null,
                ClientGUID = client.NodeGUID,
                NodeGUID = NodeGUID,
                MsgType = MessageType.CALL
            };
            ProcessCPUStateOrder(input);
        }

        public void StartMonitoringForNode(DataInput d, Node n)
        {
            DataInput input = new DataInput()
            {
                Method = GET_CPU_METHOD,
                Data = null,
                ClientGUID = d.ClientGUID,
                NodeGUID = NodeGUID,
                MsgType = MessageType.CALL,
                TaskId = MonitorTask.Item1
            };
            SendData(n, input);
        }

        private Object ProcessCPUStateOrder(DataInput input)
        {
            if (input.MsgType == MessageType.CALL)
            {
                if (MonitorTask == null)
                {
                    int newTaskID = LastTaskID;
                    MonitorTask = new Tuple<int, NodeState>(newTaskID, NodeState.WORK);
                }
                GetClientFromGUID(input.ClientGUID).Tasks.Add(new Tuple<int,NodeState>(MonitorTask.Item1,NodeState.WORK));
                input.NodeGUID = NodeGUID;
                input.TaskId = MonitorTask.Item1;
                SendDataToAllNodes(input);
            }
            else if (input.MsgType == MessageType.RESPONSE && MonitorTask != null)
            {
                foreach ( Node client in Clients)
                {
                    foreach(Tuple<int,NodeState> task in client.Tasks)
                    {
                        if(task.Item1 == MonitorTask.Item1)
                        {
                            SendData(client, input);
                        }
                    }
                }
            }
            return null;
        }

        private object RefreshTaskState(DataInput input)
        {
            
            // On fait transiter l'info au client
            SendData(GetClientFromGUID(input.ClientGUID), input);
            // Et on ne renvoit rien au Node
            return null;
        }

        #endregion

        #region Map Reduce
        protected Object ProcessMapReduce(DataInput input)
        {
            TaskExecutor executor = WorkerFactory.GetWorker(input.Method);
            Console.WriteLine("Process Map/Reduce");
            if (input.MsgType == MessageType.CALL)
            {
                LazyNodeTranfert(input);
            }
            else if (input.MsgType == MessageType.RESPONSE)
            {   

                reduce(input,executor);
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void reduce(DataInput input, TaskExecutor executor)
        {
            // Reduce
            // On cherche l'emplacement du resultat pour cette task et on l'envoit au Reduce 
            // pour y concaténer le resultat du travail du noeud
            Tuple<int, Object> result = null;
            foreach (Tuple<int, Object> tuple in Results)
            {
                if (tuple.Item1 == input.TaskId)
                {
                    result = tuple;
                }
            }
            Console.WriteLine("Result : " + result.Item1 + " " + result.Item2);
            Object reduceRes = executor.Reducer.reduce(result.Item2, input.Data);
            Console.WriteLine("Reuce res : " + ((List<Tuple<char, int>>)reduceRes).Count);
            if (TaskIsCompleted(input.TaskId, input.NodeTaskId))
            {
                // TODO check si tous les nodes ont finis
                DataInput response = new DataInput()
                {
                    TaskId = input.TaskId,
                    Method = input.Method,
                    Data = reduceRes,
                    ClientGUID = input.ClientGUID,
                    NodeGUID = this.NodeGUID,
                    MsgType = MessageType.RESPONSE,
                };
                SendData(GetClientFromGUID(input.ClientGUID), response);
            }
            else
            {
                Console.WriteLine("Task non completed");
                for (int i = 0; i < Results.Count; i++)
                {
                    if (Results[i].Item1 == input.TaskId)
                    {
                        Results[i] = new Tuple<int, Object>(input.TaskId, reduceRes);
                        Console.WriteLine("Update : " + Results[i].Item2);
                    }
                }
            }
            updateNodeTaskStatus(input.NodeTaskId, NodeState.FINISH);
        }

      

        private void LazyNodeTranfert(DataInput input)
        {
            int newTaskId = LastTaskID;
            createClientTask(input.ClientGUID, newTaskId);
            Tuple<int, Object> emptyResult = new Tuple<int, Object>(newTaskId, null);
            Results.Add(emptyResult);

            // Démarrage du thread en écoute sur la liste des nodes pour le mapping en lazzy loading
            ManualResetEvent _busy = new ManualResetEvent(false);
            TaskExecutor executor = WorkerFactory.GetWorker(input.Method);
            // On récupère le résultat du map
            IMapper mapper = executor.Mapper;
            BackgroundWorker bw = new BackgroundWorker()
            {
                WorkerSupportsCancellation = true
            };
            bw.DoWork += (o, a) =>
            {
                bool endMap = false;
                while (!endMap)
                {
                    bool allNodeWork = true;
                    // On itère sur la liste des Nodes pour leur distribuer du travail
                    for (int i = 0; i < Nodes.Count && !endMap; i++)
                    {
                        // Si au moins un node de la liste est en train d'attendre 
                        // on lui envoit du travail
                        if (Nodes[i].State == NodeState.WAIT)
                        {
                            Object data = mapper.map(input.Data);
                            if(data != null)
                            {
                                sendSubTaskToNode(Nodes[i], newTaskId, input, data);
                            }
                            else
                            {
                                // endMap vaudra true si on a déjà tout mapper
                                endMap = true;
                                // Si on a tout mapper on l'indique dans le tableau de distribution des NodeTask
                                setTaskIsMapped(newTaskId);
                            }
                            allNodeWork = false;
                        }      
                    }
                    if (allNodeWork)
                        _busy.Reset();
                }
            };

            Nodes.CollectionChanged += delegate (object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            {
                // ON récupère le node qui a changé de status dans la liste
                Node[] nodes = sender as Node[];
                // Si il est en train d'attendre on relance le map
                bool oneNodeIsWaiting = false;
                foreach(Node node in nodes)
                {
                    if (node.State == NodeState.WAIT)
                    {
                        oneNodeIsWaiting = true;
                    }
                      
                }
                if (oneNodeIsWaiting)
                {
                    _busy.Set();
                }
               
            };
            bw.RunWorkerAsync();
        }

        private void setTaskIsMapped(int newTaskId)
        {
            for(int i = 0; i < TaskDistrib.Count;i++)
            {
                if(TaskDistrib[i].Item1 == newTaskId)
                {
                    TaskDistrib[i] = new Tuple<int, List<int>, bool>(TaskDistrib[i].Item1, TaskDistrib[i].Item2, true);
                }
            }
        }

        private void sendSubTaskToNode(Node node, int newTaskID, DataInput input, Object data)
        {        
            int newNodeTaskID = LastSubTaskID;
            createNodeTasks(node, newTaskID, newNodeTaskID);
            DataInput res = new DataInput()
            {
                TaskId = newTaskID,
                NodeTaskId = newNodeTaskID,
                MsgType = MessageType.CALL,
                Method = input.Method,
                Data = data,
                ClientGUID = input.ClientGUID,
                NodeGUID = this.NodeGUID,
            };
            SendData(node, res);
        }
        private void createClientTask(string clientGUID, int newTaskID)
        {
            // Ajout de la task au client 
            Node client = GetClientFromGUID(clientGUID);
            client.Tasks.Add(new Tuple<int, NodeState>(newTaskID, NodeState.WAIT));
            // Ajout d'une ligne dans la table de ditribution des nodeTask
            TaskDistrib.Add(new Tuple<int, List<int>, bool>(newTaskID, new List<int>(),false));
        }

        private void createNodeTasks( Node node, int newTaskID, int newSubTaskID)
        {
            // On ajoute la subtask au node 
            node.Tasks.Add(new Tuple<int, NodeState>(newSubTaskID, NodeState.WAIT));
            // Ajout de la node task à la ligne de la task dans le tableau de distribution des nodetask
            for(int i = 0; i < TaskDistrib.Count; i++)
            {
                if(TaskDistrib[i].Item1 == newTaskID)
                {
                    TaskDistrib[i].Item2.Add(newSubTaskID);
                }
            }
        }

        private void updateNodeTaskStatus(int nodeTaskId, NodeState status)
        {
            foreach(Node node in Nodes)
            {
                for(int i = 0; i < node.Tasks.Count; i++)
                {
                    if(node.Tasks[i].Item1 == nodeTaskId)
                    {
                        node.Tasks[i] = new Tuple<int, NodeState>(nodeTaskId, status);
                    }
                }
            }
        }


        #endregion

        #region Utilitary methods
        private void SendNodeToClients(Node n)
        {
            List<List<String>> monitoringValues = new List<List<String>>
            {
                GetMonitoringInfos(n)
            };
            foreach (Node node in Clients)
            {
                DataInput di = new DataInput()
                {
                    ClientGUID = node.NodeGUID,
                    NodeGUID = NodeGUID,
                    Method = IDENT_METHOD,
                    Data = monitoringValues,
                    MsgType = MessageType.NODE_IDENT
                };
                SendData(node, di);

            }
        }

        protected Node GetNodeFromGUID(String guid)
        {
            foreach (Node node in Nodes)
            {
                if (node.NodeGUID.Equals(guid))
                {
                    return node;
                }
            }
            throw new Exception("Aucun node trouvé avec ce guid");
        }

        protected Node GetClientFromGUID(String guid)
        {
            foreach (Node node in Clients)
            {
                if (node.NodeGUID.Equals(guid))
                {
                    return node;
                }
            }
            throw new Exception("Aucun client trouvé avec ce guid");
        }

        public void SendNodesToClient(Node n)
        {
            List<List<String>> monitoringValues = new List<List<String>>();

            foreach (Node node in Nodes)
            {
                List<string> l = GetMonitoringInfos(node);
                if (l != null)
                {
                    monitoringValues.Add(l);
                }
            }
            if (monitoringValues.Count > 0)
            {
                DataInput di = new DataInput()
                {
                    ClientGUID = n.NodeGUID,
                    NodeGUID = NodeGUID,
                    Method = IDENT_METHOD,
                    Data = monitoringValues,
                    MsgType = MessageType.NODE_IDENT
                };
                SendData(n, di);
            }
        }

        private Node getNodeBySubTaskId(int id)
        {
            foreach (Node node in Nodes)
            {
                foreach (Tuple<int, NodeState> task in node.Tasks)
                {
                    if (task.Item1 == id)
                    {
                        return node;
                    }
                }
            }
            throw new Exception("Aucune node associé à cette subTask n'a été trouvé");
        }

        private NodeState getSubTaskState(int subtask)
        {
            foreach(Node node in Nodes)
            {
                foreach(Tuple<int,NodeState> task in node.Tasks)
                {
                    if(task.Item1 == subtask)
                    {
                        return task.Item2;
                    }
                }
            }
            throw new Exception("Aucune task trouvé pour cette subTask");
        }

        // Checker si toutes les nodes correspondant à cette task sont en etat FINISH
        private bool TaskIsCompleted(int taskId,int idCurrentTask)
        {
            bool completed = true;
            foreach (Tuple<int, List<int>,bool> task in TaskDistrib)
            {
                // On localise la bonne task dans le tableau de distribution des nodetask
                if (task.Item1 == taskId)
                {
                    // Si le booleen vaut true c'est que le mapping est terminé 
                    // donc on peut vérifier si toutes les subtask ont été process
                    if (task.Item3)
                    {
                        // On itère sur toute la liste des subtask de cette task
                        foreach (int subTask in task.Item2)
                        {
                            // Ici on vérifie qsue toutes les subtask soient en été FINISH à part celle que l'on vient de recevoir
                            if (getSubTaskState(subTask) != NodeState.FINISH && subTask != idCurrentTask)
                            {
                                completed = false;
                            }
                        }
                    }
                    // Si le booleen vaut false pas la peine de vérifier
                    else
                    {
                        completed = false;
                    }
                }
            }
            return completed;
        }
        #endregion
    }
}