using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Ragdoll físico temporário (modo de brincadeira depois da partida): cápsulas com CharacterJoints nos ossos do
    /// esqueleto procedural, só durante ~1 s. Depois o boneco levanta-se: a pose do ragdoll mistura-se de volta com a
    /// animação em 0,45 s e os corpos físicos são destruídos. Cada novo tiro reinicia o ragdoll (nunca acumula forças).
    /// </summary>
    public sealed partial class AvatarVisual
    {
        const float RagdollBlendSeconds = 0.45f;

        sealed class RagPart
        {
            public Transform Bone;
            public Rigidbody Body;
            public Quaternion LocalRotation;
        }

        readonly List<RagPart> _ragParts = new List<RagPart>();
        readonly List<Collider> _ragColliders = new List<Collider>();
        GameObject _ragRoot;
        float _ragTime = -1f;
        float _ragDuration;
        float _ragBlend = 1f;
        Vector3 _ragHipsLocal;
        Quaternion _ragRigLocal;
        // Pose de onde ficou caído, no mundo: o dono muda-se para lá (teletransporte) e os outros recebem essa posição pela
        // rede, por isso a mistura de levantar parte daqui e não da pose local (que saltava o deslocamento outra vez).
        Vector3 _ragHipsWorld;
        Quaternion _ragRigWorld;

        /// <summary>
        /// Boneco de outro cliente (jogador remoto): no fim do ragdoll o dono muda-se para onde o corpo caiu e a posição
        /// chega pela rede. Este boneco fica no chão até ela chegar (no máximo +0,5 s) e só então se levanta, já no sítio
        /// novo — se se levantasse antes, ia em direcção ao sítio antigo e saltava quando a posição chegasse.
        /// </summary>
        public bool WaitForOwnerMove { get; set; }
        bool _ownerMoved;

        /// <summary>Em ragdoll (ou ainda a levantar-se).</summary>
        public bool InRagdoll => _ragTime >= 0f;
        public bool Recovering => _ragBlend < 1f;
        /// <summary>Quanto a câmara do próprio jogador deve ir com o corpo caído: 1 no ragdoll, desce a 0 enquanto se levanta.</summary>
        public float RagdollCameraWeight => InRagdoll ? 1f : Recovering ? 1f - Mathf.SmoothStep(0f, 1f, _ragBlend) : 0f;
        /// <summary>O ragdoll acabou: posição (chão) onde a bacia ficou, para o dono mover a personagem para lá.</summary>
        public event System.Action<Vector3> RagdollEnded;
        /// <summary>Cabeça durante o ragdoll (câmara em primeira pessoa, se o jogador aceitar movimento de câmara).</summary>
        public Vector3 RagdollHead => _head != null ? _head.position : transform.position + Vector3.up * EyeHeight;

        /// <summary>Tomba com o impulso (m/s) dado, durante <paramref name="seconds"/> segundos.</summary>
        public void Ragdoll(Vector3 impulse, float seconds)
        {
            if (_rig == null || _ghost || _popped) return;
            DestroyRagdollBodies();
            _ragParts.Clear();
            _ragColliders.Clear();
            _ragRoot = new GameObject("Ragdoll_" + name);

            var pelvis = RagBody(_hips, 3f, new Vector3(0f, 0f, 0f), 0.12f, 0.24f, null);
            var torso = RagBody(_spine, 4f, new Vector3(0f, 0.2f, 0f), 0.14f, 0.42f, pelvis, 25f, 35f);
            HeadCenter(out float headRadius);
            RagSphere(_head, 2.5f, _head.InverseTransformPoint(HeadCenter(out _)), headRadius * 0.95f, torso);
            foreach (bool left in new[] { true, false })
            {
                var upper = RagBody(left ? _shoulderL : _shoulderR, 0.6f, new Vector3(0f, -0.075f, 0f), 0.05f, 0.18f, torso, 60f, 70f);
                RagBody(left ? _elbowL : _elbowR, 0.5f, new Vector3(0f, -0.09f, 0f), 0.055f, 0.22f, upper, 10f, 70f);
                var thigh = RagBody(left ? _thighL : _thighR, 1.2f, new Vector3(0f, -0.095f, 0f), 0.065f, 0.21f, pelvis, 30f, 40f);
                RagBody(left ? _kneeL : _kneeR, 1f, new Vector3(0f, -0.12f, 0.02f), 0.06f, 0.26f, thigh, 5f, 60f);
            }

            // Sem colisões entre partes do próprio boneco nem com a cápsula do jogador (evita tremores e empurrões).
            var own = transform.parent != null ? transform.parent.GetComponents<Collider>() : new Collider[0];
            for (int i = 0; i < _ragColliders.Count; i++)
            {
                foreach (var c in own) Physics.IgnoreCollision(_ragColliders[i], c);
                for (int j = i + 1; j < _ragColliders.Count; j++) Physics.IgnoreCollision(_ragColliders[i], _ragColliders[j]);
            }

            Vector3 carry = _velocity;
            foreach (var part in _ragParts)
            {
                part.Body.linearVelocity = carry;
                // Mais impulso no tronco e na cabeça: tomba para trás a partir dos pés, em vez de voar inteiro.
                float k = part.Bone == _spine || part.Bone == _head ? 1f : part.Bone == _hips ? 0.55f : 0.35f;
                part.Body.AddForce(impulse * k, ForceMode.VelocityChange);
                part.Body.AddTorque(Vector3.Cross(Vector3.up, impulse.normalized) * 4f * k, ForceMode.VelocityChange);
            }
            _ragTime = 0f;
            _ownerMoved = false;
            _ragDuration = Mathf.Max(0.3f, seconds);
            _ragBlend = 1f;
            _emoteTimer = 0f;
            _strokeRemaining = 0f;
        }

        Rigidbody RagBody(Transform bone, float mass, Vector3 center, float radius, float height, Rigidbody parent, float swing = 30f, float twist = 30f)
        {
            var go = new GameObject("Rag_" + bone.name);
            go.transform.SetParent(_ragRoot.transform, false);
            go.transform.SetPositionAndRotation(bone.position, bone.rotation);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.center = center;
            capsule.radius = radius;
            capsule.height = Mathf.Max(height, radius * 2f);
            capsule.direction = 1;
            return Attach(go, bone, mass, parent, capsule, swing, twist);
        }

        Rigidbody RagSphere(Transform bone, float mass, Vector3 center, float radius, Rigidbody parent)
        {
            var go = new GameObject("Rag_" + bone.name);
            go.transform.SetParent(_ragRoot.transform, false);
            go.transform.SetPositionAndRotation(bone.position, bone.rotation);
            var sphere = go.AddComponent<SphereCollider>();
            sphere.center = center;
            sphere.radius = radius;
            return Attach(go, bone, mass, parent, sphere, 35f, 40f);
        }

        Rigidbody Attach(GameObject go, Transform bone, float mass, Rigidbody parent, Collider collider, float swing, float twist)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.15f;
            rb.angularDamping = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 2f;
            rb.maxAngularVelocity = 25f;
            if (parent != null)
            {
                var joint = go.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.anchor = Vector3.zero;
                joint.axis = Vector3.right;
                joint.swingAxis = Vector3.forward;
                joint.lowTwistLimit = new SoftJointLimit { limit = -twist };
                joint.highTwistLimit = new SoftJointLimit { limit = twist };
                joint.swing1Limit = new SoftJointLimit { limit = swing };
                joint.swing2Limit = new SoftJointLimit { limit = swing };
                joint.enableProjection = true;
                joint.projectionDistance = 0.02f;
            }
            _ragColliders.Add(collider);
            _ragParts.Add(new RagPart { Bone = bone, Body = rb });
            return rb;
        }

        /// <summary>Durante o ragdoll os ossos seguem os corpos físicos; no fim levanta-se.</summary>
        bool UpdateRagdoll(float dt)
        {
            if (_ragTime < 0f) return false;
            _ragTime += dt;
            // Teletransporte do dono a chegar (os ossos seguem a física no mundo: o corpo caído não se mexe com ele).
            if ((transform.position - _lastPosition).sqrMagnitude > 0.25f * 0.25f) _ownerMoved = true;
            // A anca segue o corpo físico (posição e rotação); o resto só roda (os ossos não esticam).
            foreach (var part in _ragParts)
            {
                if (part.Body == null) continue;
                if (part.Bone == _hips) part.Bone.SetPositionAndRotation(part.Body.position, part.Body.rotation);
                else part.Bone.rotation = part.Body.rotation;
            }
            if (_ragTime < _ragDuration) return true;
            if (!FunModeController.Legacy && WaitForOwnerMove && !_ownerMoved && _ragTime < _ragDuration + 0.5f) return true;

            // Fim: guarda a pose e deixa a animação normal assumir, misturando (levantar-se).
            foreach (var part in _ragParts) part.LocalRotation = part.Bone.localRotation;
            _ragHipsLocal = _hips.localPosition;
            _ragRigLocal = _rig.localRotation;
            _ragHipsWorld = _hips.position;
            _ragRigWorld = _rig.rotation;
            Vector3 ground = _hips.position;
            ground.y = transform.position.y;
            DestroyRagdollBodies();
            _ragTime = -1f;
            _ragBlend = 0f;
            _baseInit = false;
            _feetInit = false;
            RagdollEnded?.Invoke(ground);
            return false;
        }

        /// <summary>Mistura a pose de onde ficou caído com a pose animada deste frame.</summary>
        void ApplyRagdollRecovery(float dt)
        {
            if (_ragBlend >= 1f) return;
            _ragBlend = Mathf.Min(1f, _ragBlend + dt / RagdollBlendSeconds);
            float k = Mathf.SmoothStep(0f, 1f, _ragBlend);
            foreach (var part in _ragParts)
                if (part.Bone != null) part.Bone.localRotation = Quaternion.Slerp(part.LocalRotation, part.Bone.localRotation, k);
            bool legacy = FunModeController.Legacy;
            // Primeiro o rig (a bacia é filha dele), depois a bacia a partir do sítio onde ficou no mundo.
            var rigFrom = legacy ? _ragRigLocal : Quaternion.Inverse(_rig.parent.rotation) * _ragRigWorld;
            _rig.localRotation = Quaternion.Slerp(rigFrom, _rig.localRotation, k);
            var hipsFrom = legacy ? _ragHipsLocal : _hips.parent.InverseTransformPoint(_ragHipsWorld);
            _hips.localPosition = Vector3.Lerp(hipsFrom, _hips.localPosition, k);
            if (_ragBlend >= 1f) _ragParts.Clear();
        }

        void DestroyRagdollBodies()
        {
            if (_ragRoot != null)
            {
                if (Application.isPlaying) Destroy(_ragRoot);
                else DestroyImmediate(_ragRoot);
            }
            _ragRoot = null;
            _ragColliders.Clear();
            foreach (var part in _ragParts) part.Body = null;
        }

        /// <summary>Corta o ragdoll já (revanche, fantasma, reconstrução do boneco).</summary>
        public void CancelRagdoll()
        {
            DestroyRagdollBodies();
            _ragParts.Clear();
            _ragTime = -1f;
            _ragBlend = 1f;
        }
    }
}
