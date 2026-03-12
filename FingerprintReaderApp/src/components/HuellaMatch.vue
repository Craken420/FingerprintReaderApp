<script setup>
import LectorHuella from './LectorHuella.vue'
</script>

<template>

<div class="dialog">

  <div class="header">
    Capturar Huella
  </div>

  <div class="huella-area">

    <div class="huella-box">
      <LectorHuella />
    </div>

  </div>

  <div class="status-container">
    <button class="status-btn">
      COINCIDENCIA
    </button>
  </div>

</div>

</template>

<style scoped>

.dialog{
  width:360px;
  height:400px;
  background:#f8fafc;
  border-radius:12px;
  overflow:hidden;
  box-shadow:0 10px 25px rgba(0,0,0,0.15);
}

.header{
  height:50px;
  display:flex;
  justify-content:center;
  align-items:center;
  font-weight:bold;
  color:white;

  background: radial-gradient(
    circle at left top,
    rgb(46,65,70),
    rgba(114,122,122,0.94)
  );
}

.huella-area{
  flex:1;
  display:flex;
  justify-content:center;
  align-items:center;
  padding:20px;
}

.huella-box{
  width:220px;
  height:220px;
  background:white;
  border-radius:8px;
  box-shadow:0 0 8px rgba(0,0,0,0.1);
}

.status-container{
  padding:10px;
}

.status-btn{
  width:100%;
  background:#22c55e;
  border:none;
  color:white;
  padding:10px;
  border-radius:6px;
  font-weight:bold;
}

</style>