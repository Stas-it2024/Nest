using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    public class User
    {
        private int _id;
        private string _username;
        private string _password;
        private string _fullname;
        private string _role;
        private string _status;

        public DateTime DateAdded { get; set; }
        public DateTime? DateDelete { get; set; }
        public int Id 
        { 
            get => _id;
            set 
            { 
                _id = value; 
            } 
        }

        public string UserName
        {
            get => _username;
            set
            {
                _username = value;
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
            }
        }

        public string FullName
        {
            get => _fullname;
            set
            {
                _fullname = value;
            }
        }

        public string Role
        {
            get => _role;
            set
            {
                _role = value;
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
            }
        }
    }
}
