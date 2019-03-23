using System;
using System.Threading.Tasks;
using System.IO;

using Jil;

using Google.Apis.Classroom.v1;
using Google.Apis.Drive.v3;

namespace classroom_scraper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var input = new StreamReader(@".\config.json");
            dynamic config =  JSON.DeserializeDynamic(input);
            input.Close();
            OAuthSignIn auth = new OAuthSignIn((string)config.clientid, (string)config.clientsecret);
            
            /*
            https://www.googleapis.com/auth/classroom.announcements	                        View and manage announcements in Google Classroom
            https://www.googleapis.com/auth/classroom.announcements.readonly	            View announcements in Google Classroom
            https://www.googleapis.com/auth/classroom.courses	                            Manage your Google Classroom classes
            https://www.googleapis.com/auth/classroom.courses.readonly	                    View your Google Classroom classes
            https://www.googleapis.com/auth/classroom.coursework.me	                        Manage your course work and view your grades in Google Classroom
            https://www.googleapis.com/auth/classroom.coursework.me.readonly    	        View your course work and grades in Google Classroom
            https://www.googleapis.com/auth/classroom.coursework.students	                Manage course work and grades for students in the Google Classroom classes you teach and view the course work and grades for classes you administer
            https://www.googleapis.com/auth/classroom.coursework.students.readonly	        View course work and grades for students in the Google Classroom classes you teach or administer
            https://www.googleapis.com/auth/classroom.guardianlinks.me.readonly	            View your Google Classroom guardians
            https://www.googleapis.com/auth/classroom.guardianlinks.students	            View and manage guardians for students in your Google Classroom classes
            https://www.googleapis.com/auth/classroom.guardianlinks.students.readonly	    View guardians for students in your Google Classroom classes
            https://www.googleapis.com/auth/classroom.profile.emails	                    View the email addresses of people in your classes
            https://www.googleapis.com/auth/classroom.profile.photos	                    View the profile photos of people in your classes
            https://www.googleapis.com/auth/classroom.push-notifications	                Receive notifications about your Google Classroom data
            https://www.googleapis.com/auth/classroom.rosters	                            Manage your Google Classroom class rosters
            https://www.googleapis.com/auth/classroom.rosters.readonly	                    View your Google Classroom class rosters
            https://www.googleapis.com/auth/classroom.student-submissions.me.readonly	    View your course work and grades in Google Classroom
            https://www.googleapis.com/auth/classroom.student-submissions.students.readonly	View course work and grades for students in the Google Classroom classes you teach or administer

            https://www.googleapis.com/auth/drive.readonly	See and download all your Google Drive files
            */
            string token = await auth.doOAuth(
                "https://www.googleapis.com/auth/classroom.courses.readonly%20" +
                "https://www.googleapis.com/auth/classroom.announcements.readonly%20" +
                "https://www.googleapis.com/auth/drive.readonly"
                );

            Console.WriteLine(String.Format("Completed login flow\nOAuth Key: {0}", token));

            ClassroomService classroomService = new ClassroomService();
            
            var request = classroomService.Courses.List();
            request.OauthToken = token;
            request.StudentId = "me";
            request.PageSize = 100;
            
            Google.Apis.Classroom.v1.Data.ListCoursesResponse response = null;
            try
            {
                response = request.Execute();
            }
            catch (Google.GoogleApiException e)
            {
                Console.WriteLine(e.HttpStatusCode);
                Console.WriteLine(e.Message);
            }

            foreach (var course in response.Courses)
            {
                var announcementsRequest = classroomService.Courses.Announcements.List(course.Id);
                announcementsRequest.OauthToken = token;
                var announcements = announcementsRequest.Execute().Announcements;
                System.IO.File.WriteAllText(String.Format(@".\output\{0}.json", course.Name), 
                JSON.SerializeDynamic(announcements));
                
                DriveService driveService = new DriveService();
                foreach (var announcement in announcements)
                {
                    if (announcement.Materials != null)
                    {
                        foreach (var material in announcement.Materials)
                        {
                            if (material.DriveFile != null)
                            {
                                var fileRequest = driveService.Files.Get(material.DriveFile.DriveFile.Id);
                                fileRequest.OauthToken = token;
                                var stream = new MemoryStream();
                                fileRequest.MediaDownloader.ProgressChanged += (Google.Apis.Download.IDownloadProgress progress) =>
                                {
                                    switch (progress.Status)
                                    {
                                        case Google.Apis.Download.DownloadStatus.Completed:
                                            {
                                                Console.WriteLine(String.Format("Downloaded {0}.", material.DriveFile.DriveFile.Title));
                                                SaveStream(stream, String.Format(@".\output\{0}\{1}", course.Name, material.DriveFile.DriveFile.Title));
                                                break;
                                            }
                                        case Google.Apis.Download.DownloadStatus.Failed:
                                            {
                                                try
                                                {
                                                    var driveFile = fileRequest.Execute();

                                                    if (config.exportmimes[driveFile.MimeType] != null)
                                                    {
                                                        FilesResource.ExportRequest exportRequest = driveService.Files.Export(material.DriveFile.DriveFile.Id, (string)config.exportmimes[driveFile.MimeType]);
                                                        exportRequest.OauthToken = token;
                                                        exportRequest.MediaDownloader.ProgressChanged += (Google.Apis.Download.IDownloadProgress exportProgress) =>
                                                        {
                                                            switch (exportProgress.Status)
                                                            {
                                                                case Google.Apis.Download.DownloadStatus.Completed:
                                                                    {
                                                                        Console.WriteLine(String.Format("Downloaded {0}.", material.DriveFile.DriveFile.Title));
                                                                        SaveStream(stream, String.Format(@".\output\{0}\{1}.{2}", course.Name, material.DriveFile.DriveFile.Title, (string)config.mimeextensions[exportRequest.MimeType]));
                                                                        break;
                                                                    }
                                                                case Google.Apis.Download.DownloadStatus.Failed:
                                                                    {
                                                                        Console.WriteLine(String.Format("Download failed on file {0}.\n{1}", material.DriveFile.DriveFile.Title, exportProgress.Exception.Message));
                                                                        break;
                                                                    }
                                                            }
                                                        };
                                                        exportRequest.DownloadAsync(stream);
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine(String.Format("Download failed on file {0}.\n{1}", material.DriveFile.DriveFile.Title, progress.Exception.Message));
                                                    }
                                                }
                                                catch (Google.GoogleApiException e)
                                                {
                                                    Console.WriteLine(String.Format("Download failed on file {0}.\n{1}", material.DriveFile.DriveFile.Title, e.Message));
                                                }
                                                
                                                break;
                                            }
                                    }
                                };
                                
                                // Synchronous for now
                                await fileRequest.DownloadAsync(stream);
                            }
                            //Console.WriteLine(material.DriveFile.DriveFile.AlternateLink);
                        }
                    }
                }
            }
            
        }
        static void SaveStream(MemoryStream stream, string saveTo)
        {
            saveTo = Path.GetDirectoryName(saveTo) + @"\" + String.Concat(Path.GetFileName(saveTo).Split(Path.GetInvalidFileNameChars()));
            Directory.CreateDirectory(Path.GetDirectoryName(saveTo));
            using (FileStream file = new FileStream(saveTo, FileMode.Create, FileAccess.Write))
            {
                stream.WriteTo(file);
            }
        }
    }
}
