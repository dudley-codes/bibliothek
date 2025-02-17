﻿using Bibliotech.Models;
using Bibliotech.Utils;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Bibliotech.Repositories
{
    public class LoanRepository : BaseRepository, ILoanRepository
    {
        public LoanRepository(IConfiguration configuration) : base(configuration) { }

        public void Delete(int id, UserProfile user)
        {
            using (var conn = Connection)
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM Loan WHERE Id = @id AND BorrowerId = @currentUserId";
                    DbUtils.AddParameter(cmd, "@id", id);
                    DbUtils.AddParameter(cmd, "@currentUserId", user.Id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void Add(Loan loan, UserProfile user)
        {
            using (var conn = Connection)
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO Loan(
                                        BookId,
                                        OwnerId,
                                        BorrowerId,
                                        RequestDate,
                                        DueDate,
                                        LoanStatusId)
                                        OUTPUT INSERTED.ID
                                        VALUES(@bookId, @ownerId, @borrowerId, @requestDate, @dueDate, @loanStatusId)";

                    DateTimeOffset dtOffset = DateTimeOffset.FromUnixTimeSeconds(loan.DueDateUnix);

                    DbUtils.AddParameter(cmd, "@bookId", loan.BookId);
                    DbUtils.AddParameter(cmd, "@ownerId", loan.OwnerId);
                    DbUtils.AddParameter(cmd, "@borrowerId", user.Id);
                    DbUtils.AddParameter(cmd, "@requestDate", DateTime.Now);
                    DbUtils.AddParameter(cmd, "@dueDate", dtOffset.DateTime);
                    DbUtils.AddParameter(cmd, "@loanStatusId", 1);

                    loan.Id = (int)cmd.ExecuteScalar();
                }
            }
        }
        /// <summary>
        /// Update book loan status
        /// </summary>
        /// <param name="loan"></param>
        public void UpdateLoanStatus(Loan loan)
        {
            using (var conn = Connection)
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {

                    cmd.CommandText = @"SELECT Id, Status FROM LoanStatus";

                    var reader = cmd.ExecuteReader();
                    var statusList = new List<LoanStatus>();
                    while (reader.Read())
                    {
                        var status = new LoanStatus()
                        {
                            Id = DbUtils.GetInt(reader, "Id"),
                            Status = DbUtils.GetString(reader, "Status")
                        };

                        statusList.Add(status);
                    }

                    reader.Close();
                    //sets the loanStatusId to match stutus Id of selected status
                    foreach (LoanStatus status in statusList)
                    {
                        if (status.Status.ToLower() == loan.LoanStatus.Status.ToLower())
                        {
                            loan.LoanStatusId = status.Id;
                        }
                    }

                    string StatusDate = null;

                    if (loan.LoanStatus.Status == "IsReturned")
                    {
                        StatusDate = "ReturnDate = @dateTime";
                    }
                    else if (loan.LoanStatus.Status == "IsApproved")
                    {
                        StatusDate = @"ResponseDate = @dateTime,
                                        DueDate = @dueDate";

                        DateTimeOffset dtOffset = DateTimeOffset.FromUnixTimeSeconds(loan.DueDateUnix);
                        DbUtils.AddParameter(cmd, "@dueDate", dtOffset.DateTime);
                    }
                    else
                    {
                        StatusDate = "ResponseDate = @dateTime";
                    }

                    cmd.CommandText = @$"
                                        UPDATE Loan
                                                SET LoanStatusId = @loanStatusId,
                                                {StatusDate}
                                                WHERE Id = @id
                                        ";

                    DbUtils.AddParameter(cmd, "@loanStatusId", loan.LoanStatusId);
                    DbUtils.AddParameter(cmd, "@dateTime", DateTime.Now);
                    DbUtils.AddParameter(cmd, "@id", loan.Id);

                    cmd.ExecuteNonQuery();
                }
            }
        }
        /// <summary>
        /// Loan requests for user's books by user and loan ID
        /// </summary>
        /// <param name="user"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public List<Loan> GetLoansByCurrentUser(UserProfile user, int id)
        {
            using (var conn = Connection)
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                                         SELECT
                                                b.Id AS BookId, 
                                                b.Title,
                                                b.Description, 
                                                b.AverageRating,  
                                                b.OnShelf, 
                                                b.ThumbnailUrl, 
                                                a.Name AS Author,
                                                b.OwnerId,
                                                bor.Id AS BorrowerId,
                                                bor.Email AS BorrowerEmail,
                                                bor.FirstName AS BorrowerFirst,
                                                bor.LastName AS BorrowerLast,
                                                bor.ImageUrl AS BorrowerImageUrl,
                                                bor.City AS BorrowerCity,
                                                bor.State AS BorrowerState,
                                                up.Id AS UserProfileId,
                                                up.Email,
                                                up.FirstName,
                                                up.LastName,
                                                up.ImageUrl,
                                                up.City,
                                                up.State,
                                                a.Id AS AuthorId,
                                                l.Id AS LoanId,
                                                l.BorrowerId,
                                                l.RequestDate,
                                                l.ResponseDate,
                                                l.DueDate,
                                                l.ReturnDate,
                                                ls.Id AS LoanStatusId,
                                                ls.Status
                                        FROM Loan l
                                        LEFT JOIN Book b ON b.Id = l.BookId
                                        LEFT JOIN BookAuthor ba ON ba.BookId = b.Id
                                        LEFT JOIN Author a ON ba.AuthorId = a.Id
                                        LEFT JOIN LoanStatus ls ON ls.Id = l.LoanStatusId
                                        LEFT JOIN UserProfile up on up.Id = b.OwnerId
                                        LEFT JOIN UserProfile bor ON bor.Id = l.BorrowerId 
                                        WHERE IsDeleted = 0 AND b.OwnerId = @ownerId AND b.Id = @bookId AND NOT ls.Status = 'IsReturned' AND NOT ls.Status = 'IsDenied'";

                    DbUtils.AddParameter(cmd, "@ownerId", user.Id);
                    DbUtils.AddParameter(cmd, "@bookId", id);

                    var reader = cmd.ExecuteReader();

                    var loans = new List<Loan>();
                    while (reader.Read())
                    {
                        var loanId = DbUtils.GetInt(reader, "LoanId");
                        //Checks to see if book has been added to list if not, creates book object
                        var existingLoan = loans.FirstOrDefault(p => p.Id == loanId);
                        if (existingLoan == null)
                        {
                            existingLoan = new Loan()
                            {

                                Id = loanId,
                                Book = new Book()
                                {
                                    Id = DbUtils.GetInt(reader, "BookId"),
                                    Title = reader.GetString(reader.GetOrdinal("Title")),
                                    ThumbnailUrl = DbUtils.GetString(reader, "ThumbnailUrl")
                                },
                                RequestDate = DbUtils.GetDateTime(reader, "RequestDate"),
                                ResponseDate = DbUtils.GetNullableDateTime(reader, "ResponseDate"),
                                Authors = new List<Author>(),
                                Borrower = new UserProfile()
                                {
                                    Id = DbUtils.GetInt(reader, "BorrowerId"),
                                    Email = DbUtils.GetString(reader, "BorrowerEmail"),
                                    FirstName = DbUtils.GetString(reader, "BorrowerFirst"),
                                    LastName = DbUtils.GetString(reader, "BorrowerLast"),
                                    ImageUrl = DbUtils.GetNullableString(reader, "BorrowerImageUrl"),
                                    City = DbUtils.GetString(reader, "BorrowerCity"),
                                    State = DbUtils.GetString(reader, "BorrowerState")
                                },
                                Owner = new UserProfile()
                                {
                                    Id = DbUtils.GetInt(reader, "UserProfileId"),
                                    Email = DbUtils.GetString(reader, "Email"),
                                    FirstName = DbUtils.GetString(reader, "FirstName"),
                                    LastName = DbUtils.GetString(reader, "LastName"),
                                    ImageUrl = DbUtils.GetNullableString(reader, "ImageUrl"),
                                    City = DbUtils.GetString(reader, "City"),
                                    State = DbUtils.GetString(reader, "State")
                                },
                                LoanStatus = new LoanStatus()
                                {
                                    Status = DbUtils.GetString(reader, "Status")
                                }
                            };

                            loans.Add(existingLoan);
                        }
                        //Adds list of author(s) to book
                        if (DbUtils.IsNotDbNull(reader, "AuthorId"))
                        {
                            var authorId = DbUtils.GetInt(reader, "AuthorId");
                            var existingAuthor = existingLoan.Authors.FirstOrDefault(a => a.Id == authorId);

                            if (existingAuthor == null)
                            {

                                existingLoan.Authors.Add(new Author()
                                {
                                    Id = DbUtils.GetInt(reader, "AuthorId"),
                                    Name = reader.GetString(reader.GetOrdinal("Author"))
                                });
                            }
                        }

                    }

                    reader.Close();

                    return loans;
                }
            }
        }
        /// <summary>
        /// Fetches loans and loan requests made by user and loan id
        /// </summary>
        /// <param name="user"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public Loan GetLoanRequest(UserProfile user, int id)
        {
            using (var conn = Connection)
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                                         SELECT
                                                b.Id AS BookId, 
                                                b.Title,
                                                b.Description, 
                                                b.AverageRating,  
                                                b.OnShelf, 
                                                b.ThumbnailUrl, 
                                                a.Name AS Author,
                                                b.OwnerId,
                                                bor.Id AS BorrowerId,
                                                bor.Email AS BorrowerEmail,
                                                bor.FirstName AS BorrowerFirst,
                                                bor.LastName AS BorrowerLast,
                                                bor.ImageUrl AS BorrowerImageUrl,
                                                bor.City AS BorrowerCity,
                                                bor.State AS BorrowerState,
                                                up.Id AS UserProfileId,
                                                up.Email,
                                                up.FirstName,
                                                up.LastName,
                                                up.ImageUrl,
                                                up.City,
                                                up.State,
                                                a.Id AS AuthorId,
                                                l.Id AS LoanId,
                                                l.BorrowerId,
                                                l.RequestDate,
                                                l.ResponseDate,
                                                l.DueDate,
                                                l.ReturnDate,
                                                ls.Id AS LoanStatusId,
                                                ls.Status
                                        FROM Loan l
                                        LEFT JOIN Book b ON b.Id = l.BookId
                                        LEFT JOIN BookAuthor ba ON ba.BookId = b.Id
                                        LEFT JOIN Author a ON ba.AuthorId = a.Id
                                        LEFT JOIN LoanStatus ls ON ls.Id = l.LoanStatusId
                                        LEFT JOIN UserProfile up on up.Id = b.OwnerId
                                        LEFT JOIN UserProfile bor ON bor.Id = l.BorrowerId 
                                        WHERE IsDeleted = 0 AND l.BorrowerId = @currentUserId AND b.Id = @bookId";

                    DbUtils.AddParameter(cmd, "@currentUserId", user.Id);
                    DbUtils.AddParameter(cmd, "@bookId", id);

                    var reader = cmd.ExecuteReader();

                    var loan = new Loan();
                    if (reader.Read())
                    {
                        

                        loan = new Loan()
                        {

                            Id = DbUtils.GetInt(reader, "LoanId"),
                            Book = new Book()
                            {
                                Id = DbUtils.GetInt(reader, "BookId"),
                                Title = reader.GetString(reader.GetOrdinal("Title")),
                                OnShelf = reader.GetBoolean(reader.GetOrdinal("OnShelf"))
                            },
                            RequestDate = DbUtils.GetDateTime(reader, "RequestDate"),
                            ResponseDate = DbUtils.GetNullableDateTime(reader, "ResponseDate"),
                            Borrower = new UserProfile()
                            {
                                Id = DbUtils.GetInt(reader, "BorrowerId"),
                                Email = DbUtils.GetString(reader, "BorrowerEmail"),
                                FirstName = DbUtils.GetString(reader, "BorrowerFirst"),
                                LastName = DbUtils.GetString(reader, "BorrowerLast"),
                                ImageUrl = DbUtils.GetNullableString(reader, "BorrowerImageUrl"),
                                City = DbUtils.GetString(reader, "BorrowerCity"),
                                State = DbUtils.GetString(reader, "BorrowerState")
                            },
                            Owner = new UserProfile()
                            {
                                Id = DbUtils.GetInt(reader, "UserProfileId"),
                                Email = DbUtils.GetString(reader, "Email"),
                                FirstName = DbUtils.GetString(reader, "FirstName"),
                                LastName = DbUtils.GetString(reader, "LastName"),
                                ImageUrl = DbUtils.GetNullableString(reader, "ImageUrl"),
                                City = DbUtils.GetString(reader, "City"),
                                State = DbUtils.GetString(reader, "State")
                            },
                            LoanStatus = new LoanStatus()
                            {
                                Status = DbUtils.GetString(reader, "Status")
                            }
                        };
                    }

                    reader.Close();

                    return loan;
                }
            }
        }
        /// <summary>
        /// Gets all loan requests made by current logged in user
        /// </summary>
        /// <param name="user"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public List<Loan> GetAllUserLoanRequests(UserProfile user)
        {
            using (var conn = Connection)
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                                         SELECT
                                                b.Id AS BookId, 
                                                b.Title,
                                                b.IsDeleted,
                                                a.Name AS Author,
                                                b.OwnerId,
                                                b.ThumbnailUrl,
                                                bor.Id AS BorrowerId,
                                                bor.Email AS BorrowerEmail,
                                                bor.FirstName AS BorrowerFirst,
                                                bor.LastName AS BorrowerLast,
                                                up.Id AS UserProfileId,
                                                up.Email,
                                                up.FirstName,
                                                up.LastName,
                                                a.Id AS AuthorId,
                                                l.Id AS LoanId,
                                                l.RequestDate,
                                                l.ResponseDate,
                                                l.DueDate,
                                                l.ReturnDate,
                                                ls.Id AS LoanStatusId,
                                                ls.Status
                                        FROM Loan l
                                        LEFT JOIN Book b ON b.Id = l.BookId
                                        LEFT JOIN BookAuthor ba ON ba.BookId = b.Id
                                        LEFT JOIN Author a ON ba.AuthorId = a.Id
                                        LEFT JOIN LoanStatus ls ON ls.Id = l.LoanStatusId
                                        LEFT JOIN UserProfile up on up.Id = b.OwnerId
                                        LEFT JOIN UserProfile bor ON bor.Id = l.BorrowerId 
                                        WHERE l.BorrowerId = @currentUserId
                                        ORDER BY l.RequestDate DESC";

                    DbUtils.AddParameter(cmd, "@currentUserId", user.Id);

                    var reader = cmd.ExecuteReader();

                    var loans = new List<Loan>();
                    while (reader.Read())
                    {
                        var loanId = DbUtils.GetInt(reader, "LoanId");
                        //Checks to see if book has been added to list if not, creates book object
                        var existingLoan = loans.FirstOrDefault(p => p.Id == loanId);
                        if (existingLoan == null)
                        {
                            existingLoan = new Loan()
                            {

                                Id = loanId,
                                Book = new Book()
                                {
                                    Id = DbUtils.GetInt(reader, "BookId"),
                                    Title = reader.GetString(reader.GetOrdinal("Title")),
                                    IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
                                    Authors = new List<Author>(),
                                    ThumbnailUrl = DbUtils.GetString(reader, "ThumbnailUrl")
                                },
                                RequestDate = DbUtils.GetDateTime(reader, "RequestDate"),
                                ResponseDate = DbUtils.GetNullableDateTime(reader, "ResponseDate"),
                                ReturnDate = DbUtils.GetNullableDateTime(reader, "ReturnDate"),
                                DueDate = DbUtils.GetNullableDateTime(reader, "DueDate"),
                                Borrower = new UserProfile()
                                {
                                    Id = DbUtils.GetInt(reader, "BorrowerId"),
                                    Email = DbUtils.GetString(reader, "BorrowerEmail"),
                                    FirstName = DbUtils.GetString(reader, "BorrowerFirst"),
                                    LastName = DbUtils.GetString(reader, "BorrowerLast"),

                                },
                                Owner = new UserProfile()
                                {
                                    Id = DbUtils.GetInt(reader, "UserProfileId"),
                                    Email = DbUtils.GetString(reader, "Email"),
                                    FirstName = DbUtils.GetString(reader, "FirstName"),
                                    LastName = DbUtils.GetString(reader, "LastName"),
                                },
                                LoanStatus = new LoanStatus()
                                {
                                    Status = DbUtils.GetString(reader, "Status")
                                }
                            };

                            loans.Add(existingLoan);
                        }

                        if (DbUtils.IsNotDbNull(reader, "AuthorId"))
                        {
                            var authorId = DbUtils.GetInt(reader, "AuthorId");
                            var existingAuthor = existingLoan.Book.Authors.FirstOrDefault(a => a.Id == authorId);

                            if (existingAuthor == null)
                            {

                                existingLoan.Book.Authors.Add(new Author()
                                {
                                    Id = DbUtils.GetInt(reader, "AuthorId"),
                                    Name = reader.GetString(reader.GetOrdinal("Author"))
                                });
                            }
                        }

                    }

                    reader.Close();

                    return loans;
                }
            }
        }
        /// <summary>
        /// Get all loans but the loan to be deleted
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public List<Loan> GetAllButDeleted(UserProfile user, int id)
        {
            using (var conn = Connection)
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                                         SELECT
                                                b.Id AS BookId, 
                                                b.Title,
                                                b.IsDeleted,
                                                b.ThumbnailUrl,
                                                a.Name AS Author,
                                                b.OwnerId,
                                                bor.Id AS BorrowerId,
                                                bor.Email AS BorrowerEmail,
                                                bor.FirstName AS BorrowerFirst,
                                                bor.LastName AS BorrowerLast,
                                                up.Id AS UserProfileId,
                                                up.Email,
                                                up.FirstName,
                                                up.LastName,
                                                a.Id AS AuthorId,
                                                l.Id AS LoanId,
                                                l.BorrowerId,
                                                l.RequestDate,
                                                l.ResponseDate,
                                                l.DueDate,
                                                l.ReturnDate,
                                                ls.Id AS LoanStatusId,
                                                ls.Status
                                        FROM Loan l
                                        LEFT JOIN Book b ON b.Id = l.BookId
                                        LEFT JOIN BookAuthor ba ON ba.BookId = b.Id
                                        LEFT JOIN Author a ON ba.AuthorId = a.Id
                                        LEFT JOIN LoanStatus ls ON ls.Id = l.LoanStatusId
                                        LEFT JOIN UserProfile up on up.Id = b.OwnerId
                                        LEFT JOIN UserProfile bor ON bor.Id = l.BorrowerId 
                                        WHERE l.BorrowerId = @currentUserId AND NOT l.Id = @deletedId
                                        ORDER BY l.RequestDate DESC";

                    DbUtils.AddParameter(cmd, "@currentUserId", user.Id);
                    DbUtils.AddParameter(cmd, "@deletedId", id);

                    var reader = cmd.ExecuteReader();

                    var loans = new List<Loan>();
                    while (reader.Read())
                    {
                        var loanId = DbUtils.GetInt(reader, "LoanId");
                        //Checks to see if book has been added to list if not, creates book object
                        var existingLoan = loans.FirstOrDefault(p => p.Id == loanId);
                        if (existingLoan == null)
                        {
                            existingLoan = new Loan()
                            {

                                Id = loanId,
                                Book = new Book()
                                {
                                    Id = DbUtils.GetInt(reader, "BookId"),
                                    Title = reader.GetString(reader.GetOrdinal("Title")),
                                    IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
                                    ThumbnailUrl = DbUtils.GetString(reader, "ThumbnailUrl"),
                                    Authors = new List<Author>()
                                },
                                RequestDate = DbUtils.GetDateTime(reader, "RequestDate"),
                                ResponseDate = DbUtils.GetNullableDateTime(reader, "ResponseDate"),
                                DueDate = DbUtils.GetNullableDateTime(reader, "DueDate"),
                                ReturnDate = DbUtils.GetNullableDateTime(reader, "ReturnDate"),
                                Borrower = new UserProfile()
                                {
                                    Id = DbUtils.GetInt(reader, "BorrowerId"),
                                    Email = DbUtils.GetString(reader, "BorrowerEmail"),
                                    FirstName = DbUtils.GetString(reader, "BorrowerFirst"),
                                    LastName = DbUtils.GetString(reader, "BorrowerLast")
                                },
                                Owner = new UserProfile()
                                {
                                    Id = DbUtils.GetInt(reader, "UserProfileId"),
                                    Email = DbUtils.GetString(reader, "Email"),
                                    FirstName = DbUtils.GetString(reader, "FirstName"),
                                    LastName = DbUtils.GetString(reader, "LastName"),

                                },
                                LoanStatus = new LoanStatus()
                                {
                                    Status = DbUtils.GetString(reader, "Status")
                                }
                            };

                            loans.Add(existingLoan);
                        }

                        if (DbUtils.IsNotDbNull(reader, "AuthorId"))
                        {
                            var authorId = DbUtils.GetInt(reader, "AuthorId");
                            var existingAuthor = existingLoan.Book.Authors.FirstOrDefault(a => a.Id == authorId);

                            if (existingAuthor == null)
                            {

                                existingLoan.Book.Authors.Add(new Author()
                                {
                                    Id = DbUtils.GetInt(reader, "AuthorId"),
                                    Name = reader.GetString(reader.GetOrdinal("Author"))
                                });
                            }
                        }

                    }

                    reader.Close();

                    return loans;
                }
            }
        }

        /// <summary>
        /// Get all loans made to current user for their books
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public List<Loan> GetRequestsMadeToUser(UserProfile user)
        {
            using (var conn = Connection)
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                                         SELECT
                                                b.Id AS BookId, 
                                                b.Title,
                                                b.Description, 
                                                b.AverageRating,  
                                                b.OnShelf, 
                                                b.ThumbnailUrl,
                                                b.IsDeleted,
                                                a.Name AS Author,
                                                b.OwnerId,
                                                bor.Id AS BorrowerId,
                                                bor.Email AS BorrowerEmail,
                                                bor.FirstName AS BorrowerFirst,
                                                bor.LastName AS BorrowerLast,
                                                bor.ImageUrl AS BorrowerImageUrl,
                                                bor.City AS BorrowerCity,
                                                bor.State AS BorrowerState,
                                                up.Id AS UserProfileId,
                                                up.Email,
                                                up.FirstName,
                                                up.LastName,
                                                up.ImageUrl,
                                                up.City,
                                                up.State,
                                                a.Id AS AuthorId,
                                                l.Id AS LoanId,
                                                l.BorrowerId,
                                                l.RequestDate,
                                                l.ResponseDate,
                                                l.DueDate,
                                                l.ReturnDate,
                                                ls.Id AS LoanStatusId,
                                                ls.Status
                                        FROM Loan l
                                        LEFT JOIN Book b ON b.Id = l.BookId
                                        LEFT JOIN BookAuthor ba ON ba.BookId = b.Id
                                        LEFT JOIN Author a ON ba.AuthorId = a.Id
                                        LEFT JOIN LoanStatus ls ON ls.Id = l.LoanStatusId
                                        LEFT JOIN UserProfile up on up.Id = b.OwnerId
                                        LEFT JOIN UserProfile bor ON bor.Id = l.BorrowerId 
                                        WHERE b.OwnerId = @currentUserId AND NOT ls.Status = 'IsDenied' 
                                        AND NOT ls.Status = 'IsReturned' AND b.IsDeleted = 0
                                        ORDER BY l.RequestDate DESC";

                    DbUtils.AddParameter(cmd, "@currentUserId", user.Id);

                    var reader = cmd.ExecuteReader();

                    var loans = new List<Loan>();
                    while (reader.Read())
                    {
                        var loanId = DbUtils.GetInt(reader, "LoanId");
                        //Checks to see if book has been added to list if not, creates book object
                        var existingLoan = loans.FirstOrDefault(p => p.Id == loanId);

                        if (existingLoan == null)
                        {

                            existingLoan = new Loan()
                            {

                                Id = DbUtils.GetInt(reader, "LoanId"),
                                Book = new Book()
                                {
                                    Id = DbUtils.GetInt(reader, "BookId"),
                                    Title = reader.GetString(reader.GetOrdinal("Title")),
                                    IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
                                    ThumbnailUrl = DbUtils.GetString(reader, "ThumbnailUrl"),
                                    Authors = new List<Author>()
                                },
                                RequestDate = DbUtils.GetDateTime(reader, "RequestDate"),
                                ResponseDate = DbUtils.GetNullableDateTime(reader, "ResponseDate"),
                                DueDate = DbUtils.GetNullableDateTime(reader, "DueDate"),
                                Borrower = new UserProfile()
                                {
                                    Id = DbUtils.GetInt(reader, "BorrowerId"),
                                    Email = DbUtils.GetString(reader, "BorrowerEmail"),
                                    FirstName = DbUtils.GetString(reader, "BorrowerFirst"),
                                    LastName = DbUtils.GetString(reader, "BorrowerLast"),
                                    ImageUrl = DbUtils.GetNullableString(reader, "BorrowerImageUrl"),
                                    City = DbUtils.GetString(reader, "BorrowerCity"),
                                    State = DbUtils.GetString(reader, "BorrowerState")
                                },
                                Owner = new UserProfile()
                                {
                                    Id = DbUtils.GetInt(reader, "UserProfileId"),
             
                                    Email = DbUtils.GetString(reader, "Email"),
                                    FirstName = DbUtils.GetString(reader, "FirstName"),
                                    LastName = DbUtils.GetString(reader, "LastName"),
                                    ImageUrl = DbUtils.GetNullableString(reader, "ImageUrl"),
                                    City = DbUtils.GetString(reader, "City"),
                                    State = DbUtils.GetString(reader, "State")
                                },
                                LoanStatus = new LoanStatus()
                                {
                                    Status = DbUtils.GetString(reader, "Status")
                                }
                            };
                            loans.Add(existingLoan);
                        }

                        if (DbUtils.IsNotDbNull(reader, "AuthorId"))
                        {
                            var authorId = DbUtils.GetInt(reader, "AuthorId");
                            var existingAuthor = existingLoan.Book.Authors.FirstOrDefault(a => a.Id == authorId);

                            if (existingAuthor == null)
                            {

                                existingLoan.Book.Authors.Add(new Author()
                                {
                                    Id = DbUtils.GetInt(reader, "AuthorId"),
                                    Name = reader.GetString(reader.GetOrdinal("Author"))
                                });
                            }
                        }

                    }

                    reader.Close();

                    return loans;
                }
            }
        }




    }
}
