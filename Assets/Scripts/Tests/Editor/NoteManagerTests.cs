using System.Collections.Generic;
using NoteSystem;
using Characters;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    public class NoteManagerTests
    {
        private GameObject _go;
        private NoteManager _noteManager;

        [SetUp]
        public void Setup()
        {
            _go = new GameObject("NoteManager");
            _noteManager = _go.AddComponent<NoteManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void AddNote_AddsRoleToCorrectCategory()
        {
            ulong playerId = 123;
            Role role = new Role { roleID = RoleID.Omniscient };
            
            _noteManager.AddNote(playerId, role, NoteType.Possible);
            
            var notes = _noteManager.GetNotesForPlayer(playerId, NoteType.Possible);
            Assert.AreEqual(1, notes.Count);
            Assert.AreEqual(RoleID.Omniscient, notes[0].roleID);
        }

        [Test]
        public void AddNote_DoesNotAddDuplicateRole()
        {
            ulong playerId = 123;
            Role role = new Role { roleID = RoleID.Omniscient };
            
            _noteManager.AddNote(playerId, role, NoteType.Possible);
            _noteManager.AddNote(playerId, role, NoteType.Possible);
            
            var notes = _noteManager.GetNotesForPlayer(playerId, NoteType.Possible);
            Assert.AreEqual(1, notes.Count, "Duplicate roles should not be added to the same note type");
        }

        [Test]
        public void RemoveNote_RemovesRoleFromCategory()
        {
            ulong playerId = 123;
            Role role = new Role { roleID = RoleID.Omniscient };
            _noteManager.AddNote(playerId, role, NoteType.Excluded);
            
            _noteManager.RemoveNote(playerId, role, NoteType.Excluded);
            
            var notes = _noteManager.GetNotesForPlayer(playerId, NoteType.Excluded);
            Assert.IsEmpty(notes);
        }

        [Test]
        public void GetNotesForPlayer_ReturnsEmptyList_WhenNoNotes()
        {
            var notes = _noteManager.GetNotesForPlayer(999, NoteType.Confirmed);
            Assert.IsNotNull(notes);
            Assert.IsEmpty(notes);
        }
    }
}
