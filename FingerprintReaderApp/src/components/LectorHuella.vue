<script setup>
import { ref } from 'vue'

let connection = null

function conectarWS(){

  connection = new WebSocket("ws://localhost:5000/ws")

  connection.onopen = ()=>{
    console.log("WS conectado")
  }

  connection.onmessage = mensajeWS

  connection.onclose = ()=>{
    console.log("WS cerrado")
  }

}

function mensajeWS(event){

  const datos = JSON.parse(event.data)

  console.log(datos)

  if(datos?.Imagen){

    document.querySelector('#image').src =
    `data:image/png;base64,${datos.Imagen}`

  }

}

function capturarHuella(){

  connection.send(JSON.stringify({
    type:"capture"
  }))

}

function compararHuella(){

  const huellaEjemplo =
  "Rk1EAAAAAQAAABIAAQAAACkAAABWAAABVwAAAFMAAAABAAAAAQAAA..."

  connection.send(JSON.stringify({
    type:"match",
    huella:huellaEjemplo
  }))

}
</script>


<template>

<div class="lector">

  <div class="btns">

    <button @click="conectarWS">
      Conectar
    </button>

    <button @click="capturarHuella">
      Capturar
    </button>

    <button @click="compararHuella">
      Match
    </button>

  </div>

  <div class="container-center">

    <div class="image-container">

      <img
      id="image"
      class="image-display"
      />

    </div>

  </div>

</div>

</template>


<style scoped>

.lector{
  width:100%;
  height:100%;
  display:flex;
  flex-direction:column;
}

.btns{
  display:flex;
  gap:5px;
  justify-content:center;
  margin-bottom:10px;
}

.btns button{
  padding:6px 10px;
  border:none;
  background:#334155;
  color:white;
  border-radius:4px;
  cursor:pointer;
}

.btns button:hover{
  background:#1e293b;
}

.container-center{
  flex:1;
  display:flex;
  justify-content:center;
  align-items:center;
}

.image-container{
  width:100%;
  height:100%;
  background:white;
}

.image-display{
  width:100%;
  height:100%;
  object-fit:contain;
  border-radius:6px;
}

</style>