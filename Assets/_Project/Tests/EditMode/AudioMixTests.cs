using System.Reflection;
using FriendSlop.Core;
using FriendSlop.Match;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class AudioMixTests
    {
        GameObject _root;
        SfxService _service;
        AudioSource _crowd, _room, _machine, _tension, _roll;
        GameSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Audio QA");
            _service = _root.AddComponent<SfxService>();
            _crowd = Source("_murmur"); _room = Source("_roomTone");
            _machine = Source("_machineHum"); _tension = Source("_tension"); _roll = Source("_roll");
            _settings = new GameSettings { MasterVolume = 1f, SfxVolume = 1f, MusicVolume = 1f };
        }

        AudioSource Source(string field)
        {
            var child = new GameObject(field); child.transform.SetParent(_root.transform);
            var source = child.AddComponent<AudioSource>(); source.playOnAwake = false;
            typeof(SfxService).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_service, source);
            return source;
        }

        void Mix(bool arena = true, bool match = true, MatchPhase phase = MatchPhase.PunishmentDraw)
        {
            typeof(SfxService).GetMethod("ApplyMix", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_service, new object[] { _settings, arena, match, phase, 1f / 60 });
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void MasterMuteImmediatelySilencesEveryContinuousSource()
        {
            Mix(); _settings.MasterVolume = 0f; Mix();
            foreach (var source in new[] { _crowd, _room, _machine, _tension, _roll })
                Assert.That(source.volume, Is.Zero, source.name);
        }

        [Test]
        public void CategoryVolumesAlsoControlMachineAndActiveRoll()
        {
            Mix(); _settings.SfxVolume = 0f; Mix();
            foreach (var source in new[] { _crowd, _room, _machine, _roll }) Assert.That(source.volume, Is.Zero);
            Assert.That(_tension.volume, Is.GreaterThan(0f));
            _settings.MusicVolume = 0f; Mix(); Assert.That(_tension.volume, Is.Zero);
        }

        [Test]
        public void ReturningToMenuSilencesLoopsEvenWithoutPhaseEvent()
        {
            Mix(); Mix(false, false);
            foreach (var source in new[] { _crowd, _room, _machine, _tension })
            {
                Assert.That(source.volume, Is.Zero);
                Assert.That(source.isPlaying, Is.False);
            }
        }

        [TestCase(MatchPhase.AwaitShot)] [TestCase(MatchPhase.MatchOver)]
        public void SuspenseDoesNotLeakIntoOtherPhases(MatchPhase phase)
        {
            Mix(); Assert.That(_tension.volume, Is.GreaterThan(0f));
            Mix(true, true, phase); Assert.That(_tension.volume, Is.Zero);
        }

        [Test]
        public void ReturningToLobbyEndsSuspenseButKeepsQuietAmbience()
        {
            Mix(); Mix(true, false);
            Assert.That(_tension.volume, Is.Zero);
            Assert.That(_room.volume, Is.InRange(.001f, .01f));
            Assert.That(_machine.volume, Is.InRange(.001f, .03f));
        }
    }
}
