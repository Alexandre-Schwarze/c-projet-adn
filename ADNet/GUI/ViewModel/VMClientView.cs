﻿using c_projet_adn.Network.Impl;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using NodeNet.GUI.ViewModel;
using System;
using System.Windows.Input;

namespace ADNet.GUI.ViewModel
{
    public class VMClientView : ViewModelBase
    {
        #region Properties
        public ICommand WindowLoaded { get; set; }
        public ICommand ICommandBtnSend { get; set; }
        private DNAClient client;

        private String txtMsg;
        public String TxtMsgProp
        {
            get
            {
                return txtMsg;
            }
            set
            {
                txtMsg = value;
                RaisePropertyChanged("TxtMsgProp");
            }
        }

        private string clientResponse;

        public string ClientResponse
        {
            get { return clientResponse; }
            set
            {
                clientResponse = value;
                RaisePropertyChanged("ClientResponse");
            }
        }
        #endregion

        public VMMonitoringUC UcVmMonitoring { get; set; }
        public VMClientView()
        {
            WindowLoaded = new RelayCommand(OnLoad);
            UcVmMonitoring = NodeNet.GUI.ViewModel.ViewModelLocator.VMLMonitorUcStatic;
            ICommandBtnSend = new RelayCommand(SendMessage);
        }

        public void OnLoad()
        {
            Console.WriteLine("Connect client onload");
            client = new DNAClient("Client","127.0.0.1",3001);
            client.Connect("127.0.0.1", 3000);
            Console.WriteLine("Connect client onload end");
        }

        public void SendMessage()
        {
            Console.WriteLine("Sending message " + txtMsg + " to all clients");
            client.SendMessage(txtMsg);
        }

        public void SetMessage(string s)
        {
            ClientResponse = s;
        }
    }
}
