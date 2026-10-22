// Copyright (c) 2026 DeskPilot contributors. Project MS-CL.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Terminals.Security;
using Terminals.Data.DB;
using System.Data.Entity.Infrastructure;
using System.Data.Entity;
using System.Data.Entity.Core.Metadata.Edm;
namespace Terminals
{
    internal partial class MainForm
    {
        private void RunReleaseCompatibilitySmoke(string report)
        {
            // Public ASCII fixtures, independently calculated with OpenSSL AES-192-CBC
            // and Python SHA-512(UTF-16LE), not a self-generated round trip.
            var key=Encoding.ASCII.GetBytes("012345678901234567890123");
            var iv=Encoding.ASCII.GetBytes("0123456789012345");
            const string ciphertext="fIK5bW4xLwvPZBmJj/w7J4PjkCCS04z0+w/qGzfuIO0=";
            const string plain="DeskPilot legacy fixture";
            FolderAssert(Encoding.ASCII.GetString(PasswordFunctions.DecryptByKey(Convert.FromBase64String(ciphertext),iv,key))==plain,"Old Rijndael credential fixture did not decrypt");
            FolderAssert(Convert.ToBase64String(PasswordFunctions.EncryptByKey(Encoding.ASCII.GetBytes(plain),iv,key))==ciphertext,"Legacy importer encryption changed its byte format");
            FolderAssert(PasswordFunctions.ComputeMasterPasswordHash(plain)=="19e6dc59f67d8ccc65a2fd90ffde5eaf89e38bbacda6468d1c5d05609f2cd1b82b19b7f61a80d251184f2ddbcf440093bfb3d895a63e79bd962ad87724335a3e","Legacy SHA512 encoding changed");
            FolderAssert(PasswordFunctions2.DecryptPassword("MDEyMzQ1Njc4OTAxMjM0NXYCR6Q00X0gR4vq/ORYsgf0J0f9VInKjoOCwERdpfW+sjUr+KFHcJGC+U/Lg/CRyAu0Sp1DqJiavTLINKUm5tMfF8Sqqnu2m63UgPJhF2I+","MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=")=="DeskPilot credential — preserved","Existing UTF-16 AES-256 credential wire format changed");
            string missing=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xml");
            FolderAssert(((System.Collections.Generic.List<string>)Unified.Serialize.DeserializeXMLFromDisk(missing,typeof(System.Collections.Generic.List<string>))).Count==0,"Missing credential XML no longer constructs an empty list");
            var xml="<?xml version=\"1.0\" encoding=\"utf-8\"?><ArrayOfString xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><string>legacy &amp; retained</string></ArrayOfString>";
            FolderAssert(((System.Collections.Generic.List<string>)Unified.Serialize.DeSerializeXML(xml,typeof(System.Collections.Generic.List<string>),true)).Single()=="legacy & retained","Existing XML contract changed");
            File.AppendAllText(report,"PASS independent legacy credential ciphertext/hash vectors and retained XML/missing-file contracts\n");
            using(var form=new Form())using(var tabs=new TabControl.TabControl()) {
                FolderAssert(tabs.GetType().Assembly.GetName().Name=="DeskPilot.Tabs","Unclear third-party tab control loaded");
                form.Controls.Add(tabs);tabs.Dock=DockStyle.Fill;form.Show();
                var a=new TabControl.TabControlItem("first",new Panel());var b=new TabControl.TabControlItem("second",new Panel());
                tabs.AddTab(a);tabs.AddTab(b);var handle=a.Handle;var bounds=a.ClientRectangle;
                tabs.SelectedItem=a;tabs.SelectedItem=b;tabs.SelectedItem=a;
                FolderAssert(a.Handle==handle && a.ClientRectangle==bounds,"Tab switching recreated or resized session container");
                bool cancel=true;tabs.TabControlItemClosing+=e=>e.Cancel=cancel;tabs.CloseTab(a);
                FolderAssert(tabs.Items.Contains(a),"Cancelled close removed session");cancel=false;tabs.CloseTab(a);
                FolderAssert(!tabs.Items.Contains(a) && tabs.SelectedItem==b,"Closing session failed to select remaining tab");
                var secondHandle=b.Handle;tabs.RemoveTab(b);
                using(var detached=new TabControl.TabControl()) {detached.AddTab(b);FolderAssert(b.Handle==secondHandle,"Detaching recreated session handle");detached.RemoveTab(b);}
                tabs.AddTab(b);FolderAssert(b.Handle==secondHandle,"Reattaching recreated session handle");
                for(int i=0;i<12;i++)tabs.AddTab(new TabControl.TabControlItem("overflow "+i,null));
                tabs.GetTabItemByPoint(new System.Drawing.Point(10,10));
                FolderAssert(tabs.SelectedItem.IsDrawn,"Active overflow session has no visible tab header");
                form.Close();
            }
            File.AppendAllText(report,"PASS independent session tabs: stable handles/geometry, selection, cancelled close, close selection and detach/reattach\n");
            var transfer=new TerminalTabControlItem("CI-only popup transfer");
            terminalsControler.AddAndSelect(transfer);var transferHandle=transfer.Handle;
            terminalsControler.DetachTabToNewWindow(transfer);
            var popup=transfer.FindForm() as PopupTerminal;
            FolderAssert(popup!=null,"Session did not detach to popup");
            terminalsControler.AttachTabFromWindow(transfer);popup.Close();Application.DoEvents();
            FolderAssert(!transfer.IsDisposed && transfer.Handle==transferHandle && tcTerminals.Items.Contains(transfer) && transfer.Visible,"Closing the old popup destroyed or hid reattached session");
            terminalsControler.RemoveAndUnSelect(transfer);transfer.Dispose();Activate();
            File.AppendAllText(report,"PASS actual popup detach/reattach keeps session handle alive and visible after old window closes\n");
            // Resolve existing model metadata and SQL translation without touching
            // a user database or creating a schema. Real SQL fixture is a separate CI step.
            using(var db=DatabaseConnections.CreateInstance("Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=DeskPilotMetadataOnly;Integrated Security=True")) {
                var workspace=((IObjectContextAdapter)db).ObjectContext.MetadataWorkspace;
                FolderAssert(workspace.GetItems<EntityType>(DataSpace.CSpace).Count>0,"EF6 could not load existing model metadata");
                FolderAssert(db.Groups.Where(g=>g.Name=="compatibility-fixture").ToString().Contains("Groups"),"Existing SQL model could not translate a query");
            }
            File.AppendAllText(report,"PASS MIT-licensed EF6 reads existing SQL metadata and translates legacy model without database migration\n");
            string sql=Environment.GetEnvironmentVariable("DESKPILOT_CI_SQL");
            FolderAssert(sql!=null && sql.StartsWith(@"Data Source=(localdb)\DeskPilotBetaCi;Initial Catalog=DeskPilotCiFixture;",StringComparison.Ordinal),"Missing dedicated disposable SQL fixture");
            using(var db=DatabaseConnections.CreateInstance(sql)) {
                if(db.Database.SqlQuery<int>("SELECT COUNT(*) FROM sys.tables WHERE name='Groups'").Single()==0) {
                    // Test-only schema creation from the unchanged EDMX. Runtime
                    // initializers/migrations are disabled for all user databases.
                    string ddl=((IObjectContextAdapter)db).ObjectContext.CreateDatabaseScript();
                    db.Database.ExecuteSqlCommand(ddl);
                    string sourceRoot=Environment.GetEnvironmentVariable("DESKPILOT_CI_SOURCE_ROOT");
                    FolderAssert(!String.IsNullOrEmpty(sourceRoot),"Missing SQL fixture source root");
                    string script=Path.Combine(sourceRoot,"upstream","Terminals","Resources","Database","Terminals_CreateDatabase_v3.0.sql");
                    // The 1.0 migrations predate encrypted user/domain columns.
                    // Use the unchanged current schema's procedure definitions;
                    // create tables from the model above, never alter user data.
                    var required=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                        "InsertGroup","UpdateGroup","DeleteGroup","InsertCredentialBase","UpdateCredentialBase","DeleteCredentialBase"
                    };
                    foreach(string batch in System.Text.RegularExpressions.Regex.Split(File.ReadAllText(script),@"(?im)^\s*GO\s*$")) {
                        var match=System.Text.RegularExpressions.Regex.Match(batch.Trim(),@"^CREATE PROCEDURE \[dbo\]\.\[(\w+)\]",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if(match.Success && required.Remove(match.Groups[1].Value))db.Database.ExecuteSqlCommand(batch);
                    }
                    FolderAssert(required.Count==0,"Current SQL compatibility procedure fixtures missing");
                }
                using(var transaction=db.Database.BeginTransaction()) {
                    string name="DeskPilot-CI-"+Guid.NewGuid();
                    var group=new DbGroup {Name=name};
                    var credential=new DbCredentialBase {EncryptedUserName="fixture-user-ciphertext",EncryptedDomain="fixture-domain-ciphertext",EncryptedPassword="fixture-secret-ciphertext"};
                    db.Groups.Add(group);db.CredentialBase.Add(credential);db.SaveChanges();
                    FolderAssert(db.Groups.AsNoTracking().Single(g=>g.Name==name).Id==group.Id,"SQL folder identity round trip failed");
                    FolderAssert(db.CredentialBase.AsNoTracking().Single(c=>c.Id==credential.Id).EncryptedPassword=="fixture-secret-ciphertext","SQL credential ciphertext changed");
                    var persistedGroup=db.Groups.AsNoTracking().Single(g=>g.Id==group.Id);
                    FolderAssert(persistedGroup.Version!=null && persistedGroup.Version.Length==8,"SQL rowversion read mapping failed");
                    transaction.Rollback();
                }
            }
            File.AppendAllText(report,"PASS disposable LocalDB folder identity, concurrency rowversion and unchanged credential ciphertext SQL read/write; no user database accessed\n");

        }
    }
}
