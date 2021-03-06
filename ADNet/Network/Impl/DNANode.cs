﻿using ADNet.Map_Reduce.Node;
using NodeNet.Data;
using NodeNet.Network.Nodes;
using NodeNet.Tasks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace ADNet.Network.Impl
{
    /// <summary>
    /// Implémentation de la classe DefaultNode
    /// </summary>
    public class DnaNode : DefaultNode
    {
        /// <summary>
        /// Nom de la méthode métier pour le Module 1 
        /// </summary>
        private const string DnaQuantMethod = "DNA_QUANT";

        /// <summary>
        /// Constructeur ajoutant un TastExecutor métier à la WorkerFactory
        /// </summary>
        /// <param name="name">Nom de cet orchestrateur</param>
        /// <param name="address">Adresse IP</param>
        /// <param name="port">Port d'écoute</param>
        public DnaNode(string name, string address, int port) : base(name, address, port, true)
        {
            WorkerFactory.AddWorker(DnaQuantMethod, new TaskExecutor(this, DnaQuantStarter, new QuantStatsMapper(), new QuantStatsReducer()));
            Name = name;
            Address = address;
            Port = port;
        }

        /// <summary>
        /// Méthode de démarrage du traitement métier par l'attribution de la méthode demandé à un TaskExecutor
        /// </summary>
        /// <param name="input">Objet de transfert contenant la méthode et la donnée</param>
        /// <returns></returns>
        private object DnaQuantStarter(DataInput input)
        {
            Logger.Write(@"Démarrage de la fonction DNAQuantProcess.");
            //Thread.Sleep(1000 * input.NodeTaskId);
            TaskExecutor executor = WorkerFactory.GetWorker(input.Method);
            List<Tuple<int,char[]>> list = (List<Tuple<int, char[]>>)executor.Mapper.Map(input.Data, Environment.ProcessorCount);

            foreach (Tuple<int,char[]> t in list)
            {
                LaunchBgForWork(DnaQuantProcess, PrepareData(input, t), list.Count);
            }
            return null;
        }

        /// <summary>
        /// Méthode de comptage des caractères métiers, exécutée au sein de l'évennement RunWorkerAsync de chaque BackgroundWorker
        /// </summary>
        /// <param name="sender">Objet émetteur de l'évennement, le BackgroundWorker</param>
        /// <param name="e">Argument de l'évennement contenant la donnée à traitrer</param>
        public void DnaQuantProcess(object sender, DoWorkEventArgs e)
        {
            Tuple<int, DataInput, int> dataAndMeta = (Tuple<int, DataInput, int>)e.Argument;
            // On averti l'orchestrateur que l'on commence a process
            Tuple<int, char[]> data = (Tuple<int, char[]>)dataAndMeta.Item2.Data;
            char[] bases = { 'A', 'T', 'G', 'C', '-' };
            List<char> bufferpaires = new List<char>();
            List<char> buffersequences = new List<char>();
            List<string> listpairesbases = new List<string>()
            {
                "AT", "GC"
            };
            Dictionary<string, int> results = new Dictionary<string, int>();
            string startSeq = string.Empty;
            string endSeq = string.Empty;
            for (int i = 0; i < data.Item2.Length; i++)
            {
                if (bases.Contains(data.Item2[i]))
                {
                    // Ajout ou Mise à Jour base simple
                    if (results.TryGetValue(data.Item2[i].ToString(), out int occur))
                    {
                        results[data.Item2[i].ToString()] = occur + 1;
                    }
                    else
                    {
                        results.Add(data.Item2[i].ToString(), 1);
                    }
                    // Ajout ou Mise à Jour séquences de 4
                    if (buffersequences.Count < 4)
                    {
                        buffersequences.Add(data.Item2[i]);
                    }
                    else
                    {
                        Updateres(results, data.Item2[i], buffersequences, null);
                    }
                    // Ajout ou Mise à Jour paires de bases
                    if (bufferpaires.Count < 2)
                    {
                        bufferpaires.Add(data.Item2[i]);
                    }
                    else
                    {
                       Updateres(results, data.Item2[i], bufferpaires, listpairesbases);
                    }
                    if (i == data.Item2.Length - 1)
                    {
                        Updateres(results, data.Item2[i], bufferpaires, listpairesbases);
                        Updateres(results, data.Item2[i], buffersequences, null);
                    }

                    if (startSeq.Length < 3)
                    {
                        startSeq += data.Item2[i];
                    }
                    if (i >= data.Item2.Length - 3)
                    {
                        endSeq += data.Item2[i];
                    }
                }
            }
            dataAndMeta.Item2.Data = new Tuple<Dictionary<string, int>, string, string>(results, startSeq,endSeq);
            e.Result = dataAndMeta;
        }

        /// <summary>
        /// Méthode de mise à jour des résultats avec un caractère 
        /// </summary>
        /// <param name="results">résultats à mettre à jour</param>
        /// <param name="a">caractère</param>
        /// <param name="buffer">buffer contenant la chaine actuelle</param>
        /// <param name="listpairesbases">Liste des paires destinée à comparaison</param>
        private void Updateres(Dictionary<string, int> results, char a, List<char> buffer, List<string> listpairesbases)
        {
            string seq = string.Concat(buffer);
            if (results.TryGetValue(seq, out int occur))
            {
                results[seq] = occur + 1;
            }
            else if (buffer.Count == 4 || listpairesbases.Contains(seq))
            {
                results.Add(seq, 1);
            }

            switch (buffer.Count)
            {
                case 2:
                    buffer[0] = buffer[1];
                    buffer[1] = a;
                    break;
                case 4:
                    buffer[0] = buffer[1];
                    buffer[1] = buffer[2];
                    buffer[2] = buffer[3];
                    buffer[3] = a;
                    break;
            }
        }
    }
}
